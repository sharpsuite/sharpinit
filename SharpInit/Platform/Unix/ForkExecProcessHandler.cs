using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;

using Mono.Unix.Native;
using System.IO;

namespace SharpInit.Platform.Unix
{
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
            if (!(psi.User is UnixUserIdentifier))
                throw new InvalidOperationException();

            var user_identifier = (UnixUserIdentifier)psi.User;
            var arguments = new string[] {Path.GetFileName(psi.Path)}.Concat(psi.Arguments).Concat(new string[] { null }).ToArray();

#pragma warning disable CS0618 // Type or member is obsolete
            int fork_ret = Mono.Posix.Syscall.fork();
#pragma warning restore CS0618 // Type or member is obsolete

            if (fork_ret == 0) // child process
            {
                int error = 0;
                
                if ((error = Syscall.setgid(user_identifier.GroupId)) != 0)
                {
                    // TODO: make these communicate back to the parent process properly
                    Console.WriteLine($"setgid error: {Syscall.GetLastError()}");
                    Syscall.exit(error);
                }

                if ((error = Syscall.setuid(user_identifier.UserId)) != 0)
                {
                    Console.WriteLine($"setuid error: {Syscall.GetLastError()}");
                    Syscall.exit(error);
                }

                if(psi.WorkingDirectory != "")
                    Syscall.chdir(psi.WorkingDirectory);

                if ((error = Syscall.execv(psi.Path, arguments)) != 0)
                {
                    Console.WriteLine($"execv error: {Syscall.GetLastError()}");
                    Syscall.exit(error);
                }
            }

            if(fork_ret < 0)
            {
                throw new InvalidOperationException($"fork() returned {fork_ret}, errno: {Syscall.GetLastError()}");
            }

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
