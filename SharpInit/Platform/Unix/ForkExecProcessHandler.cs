using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;

using Mono.Unix;
using Mono.Unix.Native;
using System.IO;

namespace SharpInit.Platform.Unix
{
    /// <summary>
    /// An IProcessHandler that uses the fork() and exec() syscalls.
    /// </summary>
    [SupportedOn("unix")]
    public class ForkExecProcessHandler : IProcessHandler
    {
        public event OnProcessExit ProcessExit;
        private List<int> Processes = new List<int>();

        public ForkExecProcessHandler()
        {
            SignalHandler.ProcessExit += HandleProcessExit;
        }

        public ProcessInfo Start(ProcessStartInfo psi)
        {
            if (!(psi.User is UnixUserIdentifier) && psi.User != null)
                throw new InvalidOperationException();

            var user_identifier = (UnixUserIdentifier)(psi.User ?? new UnixUserIdentifier((int)Syscall.getuid(), (int)Syscall.getgid()));
            var arguments = new string[] {Path.GetFileName(psi.Path)}.Concat(psi.Arguments).Concat(new string[] { null }).ToArray();

            // instead of these, we just open /dev/null for now
            // this might change when we get better logging facilities
            //Syscall.pipe(out int stdout_read, out int stdout_write); // used to communicate stdout back to parent
            //Syscall.pipe(out int stderr_read, out int stderr_write); // used to communicate stderr back to parent
            int stdin_read = Syscall.open("/dev/null", OpenFlags.O_RDWR);
            int stdin_write = Syscall.open("/dev/null", OpenFlags.O_RDWR);

            int stdout_read = Syscall.open("/dev/null", OpenFlags.O_RDWR);
            int stdout_write = Syscall.open("/dev/null", OpenFlags.O_RDWR);

            int stderr_read = Syscall.open("/dev/null", OpenFlags.O_RDWR);
            int stderr_write = Syscall.open("/dev/null", OpenFlags.O_RDWR);

            Syscall.pipe(out int control_read, out int control_write); // used to communicate errors during process creation back to parent

            var stdout_w_ptr = new IntPtr(stdout_write);
            var stderr_w_ptr = new IntPtr(stderr_write);
            var control_w_ptr = new IntPtr(control_write);

#pragma warning disable CS0618 // Type or member is obsolete
            int fork_ret = Mono.Posix.Syscall.fork();
#pragma warning restore CS0618 // Type or member is obsolete

            if (fork_ret == 0) // child process
            {
                int error = 0;

                Syscall.close(stdout_read);
                Syscall.close(stderr_read);
                Syscall.close(control_read);

                var control_w_stream = new UnixStream(control_write);
                var write_to_control = (Action<string>)(_ => control_w_stream.Write(Encoding.ASCII.GetBytes(_), 0, _.Length));

                write_to_control("starting\n");

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

                if(psi.WorkingDirectory != "")
                    Syscall.chdir(psi.WorkingDirectory);

                if ((error = Syscall.execv(psi.Path, arguments)) != 0)
                {
                    write_to_control($"execv:{(int)Syscall.GetLastError()}\n");
                    Syscall.exit(1);
                }
            }

            if(fork_ret < 0)
            {
                throw new InvalidOperationException($"fork() returned {fork_ret}, errno: {Syscall.GetLastError()}");
            }

            Syscall.close(stdout_write);
            Syscall.close(stderr_write);
            Syscall.close(control_write);

            var stdout_stream = new Mono.Unix.UnixStream(stdout_read);
            var stderr_stream = new Mono.Unix.UnixStream(stderr_read);
            var control_stream = new Mono.Unix.UnixStream(control_read);

            var control_sr = new StreamReader(control_stream);

            var starting_line = control_sr.ReadLine();
            if(starting_line != "starting")
                throw new Exception($"Expected starting message from control pipe, received {starting_line}");
            
            Processes.Add(fork_ret);
            return new ProcessInfo(System.Diagnostics.Process.GetProcessById(fork_ret));
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
