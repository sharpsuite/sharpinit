using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;

using Mono.Unix;
using Mono.Unix.Native;
using System.IO;
using System.Threading;

namespace SharpInit.Platform.Unix
{
    /// <summary>
    /// An IProcessHandler that uses the fork() and exec() syscalls.
    /// </summary>
    [SupportedOn("unix")]
    public class ForkExecProcessHandler : IProcessHandler
    {
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
            if (!(psi.User is UnixUserIdentifier) && psi.User != null)
                throw new InvalidOperationException();

            var user_identifier = (UnixUserIdentifier)(psi.User ?? new UnixUserIdentifier((int)Syscall.getuid(), (int)Syscall.getgid()));
            var arguments = new string[] {Path.GetFileName(psi.Path)}.Concat(psi.Arguments).Concat(new string[] { null }).ToArray();

            Action<int> close_if_open = (Action<int>)(fd => { if (fd >= 0) {Syscall.close(fd);} });

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
                        (stdin_read, stdout_write) = CreateFileDescriptorsForStandardStreamTarget(psi, psi.StandardInputTarget, "both");
                }
                else if (file_prefixes.Any(psi.StandardErrorTarget.StartsWith))
                {
                    var in_path = psi.StandardInputTarget.Substring("file:".Length);
                    var err_path = psi.StandardErrorTarget.Substring(psi.StandardOutputTarget.IndexOf(':'));

                    if (in_path == err_path)
                        (stdin_read, stderr_write) = CreateFileDescriptorsForStandardStreamTarget(psi, psi.StandardInputTarget, "both");
                }
            }
            
            if (stdin_read == -1 && stdin_write == -1)
                (stdin_read, stdin_write) = CreateFileDescriptorsForStandardStreamTarget(psi, psi.StandardInputTarget, "input");
            
            if (stdout_read == -1 && stdout_write == -1)
                (stdout_read, stdout_write) = CreateFileDescriptorsForStandardStreamTarget(psi, psi.StandardOutputTarget, "output");
            
            if (stderr_read == -1 && stderr_write == -1)
                (stderr_read, stderr_write) = CreateFileDescriptorsForStandardStreamTarget(psi, psi.StandardErrorTarget, "error");

            Syscall.pipe(out int control_read, out int control_write); // used to communicate errors during process creation back to parent
            Syscall.fcntl(control_read, FcntlCommand.F_SETFD, 1);

            var stdout_w_ptr = new IntPtr(stdout_write);
            var stderr_w_ptr = new IntPtr(stderr_write);
            var control_w_ptr = new IntPtr(control_write);

#pragma warning disable CS0618 // Type or member is obsolete
            int fork_ret = Mono.Posix.Syscall.fork();
#pragma warning restore CS0618 // Type or member is obsolete

            if (fork_ret == 0) // child process
            {
                int error = 0;

                close_if_open(stdin_write);
                close_if_open(stdout_read);
                close_if_open(stderr_read);
                close_if_open(control_read);

                Syscall.fcntl(control_write, FcntlCommand.F_SETFD, 1);
                var control_w_stream = new UnixStream(control_write);
                var write_to_control = (Action<string>)(_ => control_w_stream.Write(Encoding.ASCII.GetBytes(_), 0, _.Length));

                write_to_control("starting\n");

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

            if(fork_ret < 0)
            {
                throw new InvalidOperationException($"fork() returned {fork_ret}, errno: {Syscall.GetLastError()}");
            }

            //var process = System.Diagnostics.Process.GetProcessById(fork_ret);
            System.Diagnostics.Process process = null;

            try { process = System.Diagnostics.Process.GetProcessById(fork_ret); } catch (Exception ex) { Console.WriteLine(ex); }

            close_if_open(stdin_read);
            close_if_open(stdout_write);
            close_if_open(stderr_write);
            close_if_open(control_write);

            var control_stream = new Mono.Unix.UnixStream(control_read);
            var control_sr = new StreamReader(control_stream);

            var timeout = (int)Math.Min(int.MaxValue, psi.Timeout.TotalMilliseconds);

            if (timeout == int.MaxValue)
                timeout = -1;

            var poll_fds = new [] {new Pollfd() { fd = control_read, events = PollEvents.POLLIN }};
            
            if (Syscall.poll(poll_fds, 1, timeout) <= 0)
            {
                Syscall.kill(fork_ret, Signum.SIGKILL);
                throw new Exception("Process launch timed out");
            }

            var starting_line = control_sr.ReadLine();
            if(starting_line != "starting")
                throw new Exception($"Expected starting message from control pipe, received {starting_line}");

            if (!psi.WaitUntilExec)
            {
                Processes.Add(fork_ret);
                return new ProcessInfo(process);
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
                    return new ProcessInfo(process);
                }
                else 
                {
                    Syscall.kill(fork_ret, Signum.SIGKILL);
                    throw new Exception($"Unexpected control data: {control_data.ToString()}");
                }
            }

            Processes.Add(fork_ret);
            return new ProcessInfo(process);
        }

        private void HandleProcessExit(int pid, int exit_code)
        {
            if (!Processes.Contains(pid))
                return;

            Processes.Remove(pid);
            ProcessExit?.Invoke(pid, exit_code);
        }
    }
}
