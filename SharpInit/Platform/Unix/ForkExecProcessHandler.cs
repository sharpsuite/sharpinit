using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;

using Mono.Unix;
using Mono.Unix.Native;
using System.IO;
using System.Threading;

using SharpInit.Units;

using NLog;

namespace SharpInit.Platform.Unix
{
    /// <summary>
    /// An IProcessHandler that uses the fork() and exec() syscalls.
    /// </summary>
    [SupportedOn("unix")]
    public class ForkExecProcessHandler : IProcessHandler
    {
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
                JournalManager = new UnixJournalManager(_service_manager.Journal);
            }
        }
        public UnixJournalManager JournalManager { get; set; }

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
                    var journal_client = JournalManager.CreateClient(psi.Unit?.UnitName ?? Path.GetFileName(psi.Path));
                    read = journal_client.ReadFd.Number;
                    write = journal_client.WriteFd.Number;
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

        public ProcessInfo Start(ProcessStartInfo psi)
        {
            HashSet<int> opened_fds = new HashSet<int>();
            Action<int> register_fd = (Action<int>)(fd => { Log.Info($"asked to register fd {fd}"); if (fd > 0) { opened_fds.Add(fd); } });
            Action<int, int> register_fd_pair = (Action<int, int>)((a, b) => { register_fd(a); register_fd(b); });
            Action<IEnumerable<int>> register_fds = (Action<IEnumerable<int>>)(fds => { foreach (var fd in fds) { register_fd(fd); } });
            Func<int, bool> close_inner = (Func<int, bool>)(fd => { Log.Info($"asked to close fd {fd}"); if (fd >= 0) { var close_ret = Syscall.close(fd); if(close_ret == 0) { return true; } Log.Warn($"close returned {close_ret} for {fd}, errno: {Syscall.GetLastError()}"); return true; } else { return false; } });
            Action<int> close_if_open = (Action<int>)(fd => { if (close_inner(fd)) { opened_fds.Remove(fd); } });
            Action clear_fds = (Action)(delegate { Log.Info($"closing {opened_fds.Count} fds: {string.Join(',', opened_fds)}"); foreach (var fd in opened_fds) { close_if_open(fd); } });

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
                register_fd_pair(stdout_read, stdout_write);
                
                if (stderr_read == -1 && stderr_write == -1)
                    (stderr_read, stderr_write) = CreateFileDescriptorsForStandardStreamTarget(psi, psi.StandardErrorTarget, "error");
                register_fd_pair(stderr_read, stderr_write);

                Syscall.pipe(out int control_read, out int control_write); // used to communicate errors during process creation back to parent
                register_fd_pair(control_read, control_write);
                Syscall.pipe(out int semaphore_read, out int semaphore_write); // used to synchronize process startup
                register_fd_pair(semaphore_read, semaphore_write);

                Log.Trace($"launch fds: {control_read} {control_write} {semaphore_read} {semaphore_write}");

                var stdout_w_ptr = new IntPtr(stdout_write);
                var stderr_w_ptr = new IntPtr(stderr_write);
                var control_w_ptr = new IntPtr(control_write);

    #pragma warning disable CS0618 // Type or member is obsolete
                int fork_ret = Mono.Posix.Syscall.fork();
    #pragma warning restore CS0618 // Type or member is obsolete

                if (fork_ret == 0) // child process
                {
                    try {
                    int error = 0;

                    close_if_open = (fd)=> { if (fd >= 0) { Syscall.close(fd);}};

                    close_if_open(stdin_write);
                    close_if_open(stdout_read);
                    close_if_open(stderr_read);
                    close_if_open(control_read);
                    close_if_open(semaphore_write);
                    close_if_open(0);
                    close_if_open(1);
                    close_if_open(2);

                    Syscall.fcntl(control_write, FcntlCommand.F_SETFD, 1);

                    var semaphore_wait_fds = new [] {new Pollfd() { fd = semaphore_read, events = PollEvents.POLLIN }};
                    Syscall.poll(semaphore_wait_fds, 1, 500);

                    if (!(semaphore_wait_fds[0].revents.HasFlag(PollEvents.POLLIN)))
                        Syscall.exit(254);

                    var control_w_stream = new UnixStream(control_write, false);
                    var write_to_control = (Action<string>)(_ => control_w_stream.Write(Encoding.ASCII.GetBytes(_), 0, _.Length));

                    write_to_control("starting\n");

                    if (Syscall.setsid() < 0)
                    {
                        write_to_control($"setsid:{(int)Syscall.GetLastError()}\n");
                        Syscall.exit(1);
                    }

                    while (Syscall.dup2(stdin_read, 0) == -1)
                    {
                        if (Syscall.GetLastError() == Errno.EINTR)
                            continue;
                        else
                        {
                            write_to_control($"dup2-stdin:{(int)Syscall.GetLastError()}\n");
                            Syscall.exit(1);
                        }
                    }

                    while (Syscall.dup2(stdout_write, 1) == -1)
                    {
                        if (Syscall.GetLastError() == Errno.EINTR)
                            continue;
                        else
                        {
                            write_to_control($"dup2-stdout:{(int)Syscall.GetLastError()}\n");
                            Syscall.exit(1);
                        }
                    }

                    while (Syscall.dup2(stderr_write, 2) == -1)
                    {
                        if (Syscall.GetLastError() == Errno.EINTR)
                            continue;
                        else
                        {
                            write_to_control($"dup2-stderr:{(int)Syscall.GetLastError()}\n");
                            Syscall.exit(1);
                        }
                    }

                    if ((error = Syscall.setgid(user_identifier.GroupId)) != 0)
                    {
                        write_to_control($"setgid:{(int)Syscall.GetLastError()}\n");
                        Syscall.exit(1);
                    }

                    if ((error = Syscall.setuid(user_identifier.UserId)) != 0)
                    {
                        write_to_control($"setuid:{(int)Syscall.GetLastError()}\n");
                        Syscall.exit(1);
                    }

                    if (psi.Environment != null)
                    {
                        if (psi.Environment.ContainsKey("LISTEN_PID") && psi.Environment["LISTEN_PID"] == "fill")
                        {
                            psi.Environment["LISTEN_PID"] = Syscall.getpid().ToString();

                            if (psi.Environment.ContainsKey("LISTEN_FDNUMS"))
                            {
                                var fd_nums = psi.Environment["LISTEN_FDNUMS"].Split(':');
                                
                                for (int i = 0, fd = 3; i < fd_nums.Length; i++, fd++)
                                {
                                    var num_str = fd_nums[i];

                                    if (int.TryParse(num_str, out int num))
                                    {
                                        Syscall.dup2(num, fd);
                                    }
                                }
                            }
                        }

                        foreach (var env_var in psi.Environment) 
                        {
                            Environment.SetEnvironmentVariable(env_var.Key, env_var.Value);
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(psi.WorkingDirectory))
                        Syscall.chdir(psi.WorkingDirectory);

                    if ((error = Syscall.execv(psi.Path, arguments)) != 0)
                    {
                        write_to_control($"execv:{(int)Syscall.GetLastError()}\n");
                        Syscall.exit(1);
                    }

                    Syscall.exit(1);
                    }
                    catch (Exception ex)
                    {
                        Syscall.exit(66);
                    }
                }

                if(fork_ret < 0)
                {
                    throw new InvalidOperationException($"fork() returned {fork_ret}, errno: {Syscall.GetLastError()}");
                }

                Log.Info($"Process started with pid {fork_ret}, path: {psi.Path}, args: [{string.Join(',', psi.Arguments.Select(arg => $"\"{arg}\""))}]");

                close_if_open(stdin_read);
                close_if_open(stdout_write);
                close_if_open(stderr_write);
                close_if_open(control_write);
                close_if_open(semaphore_read);

                //var process = System.Diagnostics.Process.GetProcessById(fork_ret);
                System.Diagnostics.Process process = null;

                try { process = System.Diagnostics.Process.GetProcessById(fork_ret); } catch (Exception ex) { Log.Error(ex); }

                var process_info = new ProcessInfo(process) { ProcessHandler = this };

                var control_stream = new Mono.Unix.UnixStream(control_read, false);
                var control_sr = new StreamReader(control_stream);

                var timeout = (int)Math.Min(int.MaxValue, psi.Timeout.TotalMilliseconds);

                if (timeout == int.MaxValue || timeout == 0)
                    timeout = -1;

                var poll_fds = new [] {new Pollfd() { fd = control_read, events = PollEvents.POLLIN }};

                var semaphore_write_stream = new Mono.Unix.UnixStream(semaphore_write, false); 
                { semaphore_write_stream.WriteByte(0); }

                Log.Debug($"sent sync signal for pid {fork_ret} startup");
                //Syscall.write(new IntPtr(semaphore_write), )

                if (Syscall.poll(poll_fds, 1, timeout) <= 0)
                {
                    Syscall.kill(fork_ret, Signum.SIGKILL);
                    throw new Exception($"pid {fork_ret} launch timed out");
                }

                Log.Debug($"pid {fork_ret} startup synchronized");

                var starting_line = control_sr.ReadLine();
                if(starting_line != "starting")
                    throw new Exception($"pid {fork_ret} expected \"starting\" message from control pipe, received \"{starting_line}\"");

                Log.Debug($"read {starting_line} for pid {fork_ret} startup");

                if (!psi.WaitUntilExec)
                {
                    Processes.Add(fork_ret);
                    ProcessInfos[fork_ret] = process_info;
                    return process_info;
                }

                var control_data = new StringBuilder();
                
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
                        Processes.Add(fork_ret);
                        ProcessInfos[fork_ret] = process_info;
                        return process_info;
                    }
                    else 
                    {
                        Syscall.kill(fork_ret, Signum.SIGKILL);
                        throw new Exception($"Unexpected control data: {control_data.ToString()}");
                    }
                }

                Processes.Add(fork_ret);
                ProcessInfos[fork_ret] = process_info;
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
            }
        }

        private void HandleProcessExit(int pid, int exit_code)
        {
            
            if (!Processes.Contains(pid))
                return;

            ProcessInfos[pid].HasExited = true;
            ProcessInfos[pid].ExitCode = exit_code;

            Processes.Remove(pid);
            ProcessInfos.Remove(pid);
            ProcessExit?.Invoke(pid, exit_code);
        }
    }
}
