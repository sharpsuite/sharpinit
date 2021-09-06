using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;

using Mono.Unix;
using Mono.Unix.Native;
using System.IO;
using System.Threading;
using System.Runtime.InteropServices;

using SharpInit.Units;

using NLog;

namespace SharpInit.Platform.Unix
{
    public unsafe struct forkhelper_t
    {
        public int stdin_fd;
        public int stdout_fd;
        public int stderr_fd;
        public int control_fd;
        public int semaphore_fd;
        public int uid;
        public int gid;
        public string binary;
        public string working_dir;
        public byte** envp;
        public byte** argv;
    }

    /// <summary>
    /// An IProcessHandler that uses the fork() and exec() syscalls.
    /// </summary>
    [SupportedOn("unix")]
    public class ForkExecProcessHandler : IProcessHandler
    {
        [DllImport("libforkhelper", EntryPoint = "augmented_fork", SetLastError = true)]
        private static extern int augmented_fork(forkhelper_t args);

        static Logger Log = LogManager.GetCurrentClassLogger();

        public event OnProcessExit ProcessExit;

        private ServiceManager _service_manager;
        public ServiceManager ServiceManager
        {
            get => _service_manager;
            set
            {
                if (_service_manager == value)
                    return;

                _service_manager = value;
                JournalManager = new UnixEpollManager("journal");
            }
        }
        public UnixEpollManager JournalManager { get; set; }

        private List<int> Processes = new List<int>();
        private Dictionary<int, ProcessInfo> ProcessInfos = new Dictionary<int, ProcessInfo>();

        public ForkExecProcessHandler()
        {
            SignalHandler.ProcessExit += HandleProcessExit;
        }

        private (int, int) CreateFileDescriptorsForStandardStreamTarget(ProcessStartInfo psi, string target, string stream)
        {
            int read = -1, write = -1;

            switch (target)
            {
                case "null":
                    read = Syscall.open("/dev/null", OpenFlags.O_RDWR);
                    write = Syscall.open("/dev/null", OpenFlags.O_RDWR);
                    break;
                case "journal":
                    var journal_client = new JournalClient(psi.Unit?.UnitName ?? Path.GetFileName(psi.Path));
                    journal_client.AllocateDescriptors();
                    
                    read = journal_client.ReadFd.Number;
                    write = journal_client.WriteFd.Number;

                    JournalManager.AddClient(journal_client);
                    break;
                case "socket":
                    if (psi.Environment.ContainsKey("LISTEN_FDNUMS"))
                    {
                        var first_fd_str = psi.Environment["LISTEN_FDNUMS"].Split(':')[0];

                        if (int.TryParse(first_fd_str, out int first_fd)) 
                        {
                            if (stream == "input")
                            {
                                read = Syscall.dup(first_fd);
                            }
                            else if (stream == "output" || stream == "error")
                            {
                                write = Syscall.dup(first_fd);
                            }
                        }
                    }
                    break;
                case "tty":
                    var tty = TtyUtilities.OpenTty((psi.Unit.Descriptor as ExecUnitDescriptor).TtyPath);
                    read = tty.FileDescriptor.Number;
                    write = tty.FileDescriptor.Number;
                    break;
            }

            if (target.StartsWith("file:") || target.StartsWith("append:") || target.StartsWith("truncate:"))
            {
                var colon_index = target.IndexOf(':');
                var path = target.Substring(colon_index + 1);
                var mode = target.Substring(0, colon_index);
                OpenFlags open_mode = 0;

                switch(stream)
                {
                    case "input":
                        open_mode = OpenFlags.O_RDONLY;
                        read = Syscall.open(path, open_mode);
                        break;
                    case "output":
                    case "error":
                        open_mode = OpenFlags.O_WRONLY;
                        break;
                    case "both":
                        open_mode = OpenFlags.O_RDWR;
                        break;
                }

                if (stream == "output" || stream == "error" || stream == "both")
                {
                    if (mode == "append")
                    {
                        open_mode |= OpenFlags.O_APPEND;
                    }

                    if (mode == "truncate")
                    {
                        open_mode |= OpenFlags.O_TRUNC;
                    }

                    if (!File.Exists(path))
                    {
                        open_mode |= OpenFlags.O_CREAT;
                    }
                    
                    write = Syscall.open(path, open_mode);

                    if (stream == "both")
                    {
                        read = Syscall.dup(write);
                    }
                }
            }

            return (read, write);
        }

        public ProcessInfo Start(ProcessStartInfo psi) => StartAsync(psi).Result;
        public async System.Threading.Tasks.Task<ProcessInfo> StartAsync(ProcessStartInfo psi)
        {
            HashSet<int> opened_fds = new HashSet<int>();
            Action<int> register_fd = (Action<int>)(fd => { if (fd > 0) { opened_fds.Add(fd); } });
            Action<int, int> register_fd_pair = (Action<int, int>)((a, b) => { register_fd(a); register_fd(b); });
            Action<IEnumerable<int>> register_fds = (Action<IEnumerable<int>>)(fds => { foreach (var fd in fds) { register_fd(fd); } });
            Func<int, bool> close_inner = (Func<int, bool>)(fd => { if (fd >= 0) { var close_ret = Syscall.close(fd); if(close_ret == 0) { return true; } Log.Warn($"close returned {close_ret} for {fd}, errno: {Syscall.GetLastError()}"); return true; } else { return false; } });
            Action<int> close_if_open = (Action<int>)(fd => { if (close_inner(fd)) { opened_fds.Remove(fd); } });
            Action clear_fds = (Action)(delegate { foreach (var fd in opened_fds) { close_if_open(fd); } });

            var helper = new forkhelper_t();
            var argv_c = -1;
            var envp_c = -1;

            try {
                if (!(psi.User is UnixUserIdentifier) && psi.User != null)
                    throw new InvalidOperationException();

                var user_identifier = (UnixUserIdentifier)(psi.User ?? new UnixUserIdentifier((int)Syscall.getuid(), (int)Syscall.getgid()));
                var arguments = new string[] {Path.GetFileName(psi.Path)}.Concat(psi.Arguments).Concat(new string[] { null }).ToArray();

                int stdin_read, stdin_write, stdout_read, stdout_write, stderr_read, stderr_write;
                stdin_read = stdin_write = stdout_read = stdout_write = stderr_read = stderr_write = -1;

                if (psi.StandardOutputTarget == "inherit")
                {
                    psi.StandardOutputTarget = psi.StandardInputTarget;
                }

                if (psi.StandardErrorTarget == "inherit")
                {
                    psi.StandardErrorTarget = psi.StandardOutputTarget;
                }

                var file_prefixes = new [] { "file:", "append:", "truncate:" };

                if (psi.StandardInputTarget.StartsWith("file:") &&
                    file_prefixes.Any(prefix => psi.StandardOutputTarget.StartsWith(prefix) || psi.StandardInputTarget.StartsWith(prefix)))
                {
                    if (file_prefixes.Any(psi.StandardOutputTarget.StartsWith))
                    {
                        var in_path = psi.StandardInputTarget.Substring("file:".Length);
                        var out_path = psi.StandardOutputTarget.Substring(psi.StandardOutputTarget.IndexOf(':'));

                        if (in_path == out_path) 
                        {
                            (stdin_read, stdout_write) = CreateFileDescriptorsForStandardStreamTarget(psi, psi.StandardInputTarget, "both");
                            register_fd_pair(stdin_read, stdout_write);
                        }
                    }
                    else if (file_prefixes.Any(psi.StandardErrorTarget.StartsWith))
                    {
                        var in_path = psi.StandardInputTarget.Substring("file:".Length);
                        var err_path = psi.StandardErrorTarget.Substring(psi.StandardOutputTarget.IndexOf(':'));

                        if (in_path == err_path)
                        {
                            (stdin_read, stderr_write) = CreateFileDescriptorsForStandardStreamTarget(psi, psi.StandardInputTarget, "both");
                            register_fd_pair(stdin_read, stderr_write);
                        }
                    }
                }
                
                if (stdin_read == -1 && stdin_write == -1)
                    (stdin_read, stdin_write) = CreateFileDescriptorsForStandardStreamTarget(psi, psi.StandardInputTarget, "input");
                
                register_fd_pair(stdin_read, stdin_write);
                
                if (stdout_read == -1 && stdout_write == -1)
                    (stdout_read, stdout_write) = CreateFileDescriptorsForStandardStreamTarget(psi, psi.StandardOutputTarget, "output");
                
                if (psi.StandardOutputTarget != "journal")
                    register_fd_pair(stdout_read, stdout_write);
                
                if (stderr_read == -1 && stderr_write == -1)
                    (stderr_read, stderr_write) = CreateFileDescriptorsForStandardStreamTarget(psi, psi.StandardErrorTarget, "error");
                
                if (psi.StandardErrorTarget != "journal")
                    register_fd_pair(stderr_read, stderr_write);

                Syscall.pipe(out int control_read, out int control_write); // used to communicate errors during process creation back to parent
                register_fd_pair(control_read, control_write);
                Syscall.pipe(out int semaphore_read, out int semaphore_write); // used to synchronize process startup
                register_fd_pair(semaphore_read, semaphore_write);

                var stdout_w_ptr = new IntPtr(stdout_write);
                var stderr_w_ptr = new IntPtr(stderr_write);
                var control_w_ptr = new IntPtr(control_write);

                helper = new forkhelper_t()
                {
                    binary = psi.Path,
                    working_dir = psi.WorkingDirectory ?? Environment.CurrentDirectory,
                    stdin_fd = stdin_read,
                    stdout_fd = stdout_write,
                    stderr_fd = stderr_write,
                    control_fd = control_write,
                    semaphore_fd = semaphore_read,
                    gid = (int)user_identifier.GroupId,
                    uid = (int)user_identifier.UserId
                };

                var argv = new string[psi.Arguments.Length + 1];
                Array.Copy(psi.Arguments, 0, argv, 1, psi.Arguments.Length);
                argv[0] = psi.Path;

                var envp = psi.Environment.Select(p => $"{p.Key}={p.Value}").ToArray();

                unsafe
                {
                    AllocNullTerminatedArray(envp, ref helper.envp);
                    envp_c = envp.Length;
                    AllocNullTerminatedArray(argv, ref helper.argv);
                    argv_c = argv.Length;
                }

                Log.Info($"Starting process with path: {psi.Path}, args: [{string.Join(',', psi.Arguments.Select(arg => $"\"{arg}\""))}]");

                var fork_ret = augmented_fork(helper);
                if(fork_ret < 0)
                {
                    throw new InvalidOperationException($"fork() returned {fork_ret}, errno: {Syscall.GetLastError()}");
                }

                Log.Info($"Process started with pid {fork_ret}, path: {psi.Path}, args: [{string.Join(',', psi.Arguments.Select(arg => $"\"{arg}\""))}]");

                if (psi.CGroup != null)
                {
                    if (!psi.CGroup.ManagedByUs)
                    {
                        Log.Warn($"pid {fork_ret} is to be joined to cgroup {psi.CGroup}, but the cgroup is not managed by us! Process won't be joined.");
                    }
                    else
                    {
                        if (!psi.CGroup.Join(fork_ret))
                        {
                            throw new Exception($"Failed to join pid {fork_ret} to cgroup {psi.CGroup}");
                        }
                    }
                }

                close_if_open(stdin_read);
                close_if_open(stdout_write);
                close_if_open(stderr_write);
                close_if_open(control_write);
                close_if_open(semaphore_read);

                System.Diagnostics.Process process = null;

                try { process = System.Diagnostics.Process.GetProcessById(fork_ret); } catch (Exception ex) { Log.Error(ex); }

                var process_info = new ProcessInfo(process) { ProcessHandler = this };
                Processes.Add(fork_ret);
                ProcessInfos[fork_ret] = process_info;

                var control_stream = new Mono.Unix.UnixStream(control_read, false);
                var control_sr = new StreamReader(control_stream);

                var timeout = (int)Math.Min(int.MaxValue, psi.Timeout.TotalMilliseconds);

                if (timeout == int.MaxValue || timeout == 0)
                    timeout = -1;

                var poll_fds = new [] {new Pollfd() { fd = control_read, events = PollEvents.POLLIN | PollEvents.POLLHUP | PollEvents.POLLERR }};

                int semaphore_start_chars = 1;
                var buf = System.Runtime.InteropServices.Marshal.AllocHGlobal(semaphore_start_chars);

                for (int i = 0; i < semaphore_start_chars; i++)
                    System.Runtime.InteropServices.Marshal.WriteByte(buf + i, 0);
                
                Log.Debug($"wrote {Syscall.write(semaphore_write, buf, (ulong)semaphore_start_chars)} bytes");

                Log.Debug($"sent sync signal for pid {fork_ret} startup");

                byte[] control_buf = new byte[1];

                var cancellation_token = new CancellationTokenSource(timeout);
                int read = await control_stream.ReadAsync(control_buf, 0, 1, cancellation_token.Token);

                if (read != 1 || cancellation_token.IsCancellationRequested)
                {
                    throw new Exception($"pid {fork_ret} launch timed out");
                }

                Log.Debug($"pid {fork_ret} startup synchronized");

                var starting_line = await control_sr.ReadLineAsync();
                if(starting_line != "starting")
                    throw new Exception($"pid {fork_ret} expected \"starting\" message from control pipe, received \"{starting_line}\"");

                Log.Debug($"read {starting_line} for pid {fork_ret} startup");

                if (!psi.WaitUntilExec)
                {
                    return process_info;
                }
                
                while (!control_sr.EndOfStream)
                    Log.Debug($"next line: {await control_sr.ReadLineAsync()}");

                /*var control_data = new StringBuilder();
                
                while ((poll_fds[0].revents & PollEvents.POLLHUP) == 0)
                {
                    var read = control_stream.ReadByte();
                    if (read == -1)
                    {
                        poll_fds[0].revents = PollEvents.POLLHUP; // kinda hacky
                        break;
                    }

                    control_data.Append((char)read);
                    poll_fds[0].revents = 0;
                }

                if (poll_fds[0].revents == PollEvents.POLLHUP) // exec() worked, or child died
                {
                    if (control_data.Length == 0) // no further control info, so assume exec worked
                    {
                        return process_info;
                    }
                    else 
                    {
                        Syscall.kill(fork_ret, Signum.SIGKILL);
                        throw new Exception($"Unexpected control data: {control_data.ToString()}");
                    }
                }*/

                return process_info;
            }
            catch
            {
                clear_fds();
                throw;
            }
            finally
            {
                clear_fds();

                unsafe
                {
                    if (envp_c != -1)
                        FreeArray(helper.envp, envp_c);
                    
                    if (argv_c != -1)
                        FreeArray(helper.argv, argv_c);
                }
            }
        }

        private void HandleProcessExit(int pid, int exit_code)
        {
            if (!Processes.Contains(pid))
            {
                Log.Info($"Received process exit for untracked pid {pid} (code {exit_code})");
                ProcessExit?.Invoke(pid, exit_code);
                return;
            }

            ProcessInfos[pid].HasExited = true;
            ProcessInfos[pid].ExitCode = exit_code;

            Processes.Remove(pid);
            ProcessInfos.Remove(pid);
            ProcessExit?.Invoke(pid, exit_code);
        }

        // The following functions are copied from dotnet/runtime/src/libraries/Common/src/Interop/Unix/System.Native/Interop.ForkAndExecProcess.cs

        /*  
        
            The MIT License (MIT)

            Copyright (c) .NET Foundation and Contributors

            All rights reserved.

            Permission is hereby granted, free of charge, to any person obtaining a copy
            of this software and associated documentation files (the "Software"), to deal
            in the Software without restriction, including without limitation the rights
            to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
            copies of the Software, and to permit persons to whom the Software is
            furnished to do so, subject to the following conditions:

            The above copyright notice and this permission notice shall be included in all
            copies or substantial portions of the Software. 
        
        */
        
        private static unsafe void AllocNullTerminatedArray(string[] arr, ref byte** arrPtr)
        {
            int arrLength = arr.Length + 1; // +1 is for null termination

            // Allocate the unmanaged array to hold each string pointer.
            // It needs to have an extra element to null terminate the array.
            arrPtr = (byte**)Marshal.AllocHGlobal(sizeof(IntPtr) * arrLength);
            System.Diagnostics.Debug.Assert(arrPtr != null);

            // Zero the memory so that if any of the individual string allocations fails,
            // we can loop through the array to free any that succeeded.
            // The last element will remain null.
            for (int i = 0; i < arrLength; i++)
            {
                arrPtr[i] = null;
            }

            // Now copy each string to unmanaged memory referenced from the array.
            // We need the data to be an unmanaged, null-terminated array of UTF8-encoded bytes.
            for (int i = 0; i < arr.Length; i++)
            {
                byte[] byteArr = Encoding.UTF8.GetBytes(arr[i]);

                arrPtr[i] = (byte*)Marshal.AllocHGlobal(byteArr.Length + 1); //+1 for null termination
                System.Diagnostics.Debug.Assert(arrPtr[i] != null);

                Marshal.Copy(byteArr, 0, (IntPtr)arrPtr[i], byteArr.Length); // copy over the data from the managed byte array
                arrPtr[i][byteArr.Length] = (byte)'\0'; // null terminate
            }
        }
        
        private static unsafe void FreeArray(byte** arr, int length)
        {
            if (arr != null)
            {
                // Free each element of the array
                for (int i = 0; i < length; i++)
                {
                    if (arr[i] != null)
                    {
                        Marshal.FreeHGlobal((IntPtr)arr[i]);
                        arr[i] = null;
                    }
                }

                // And then the array itself
                Marshal.FreeHGlobal((IntPtr)arr);
            }
        }
    }
}
