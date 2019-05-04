using Mono.Unix;
using SharpInit.Platform.Windows;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace SharpInit.Platform
{
    [SupportedOn("generic")]
    public class DefaultProcessHandler : IProcessHandler
    {
        public event OnProcessExit ProcessExit;

        public DefaultProcessHandler()
        {

        }

        public ProcessInfo StartProcess(string path, string[] arguments, string working_dir, IUserIdentifier user)
        {
            if (File.Exists(path))
                path = Path.GetFullPath(path);
            else
            {
                var env_path = Environment.GetEnvironmentVariable("PATH");
                var env_path_parts = env_path.Split(';', ':');

                foreach(var part in env_path_parts)
                {
                    if (File.Exists(Path.Combine(part, path)))
                    {
                        path = Path.Combine(part, path);
                        break;
                    }
                }
            }

            var psi = new ProcessStartInfo(path, string.Join(" ", arguments));

            if (Directory.Exists(working_dir))
                psi.WorkingDirectory = working_dir;
            
            psi.UserName = user.Username;
            psi.Domain = user.Group;

            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.RedirectStandardInput = true;

            psi.UseShellExecute = false;

            var process = Process.Start(psi);
            process.Exited += HandleProcessExit;
            process.EnableRaisingEvents = true;

            return new ProcessInfo(process);
        }

        private void HandleProcessExit(object sender, EventArgs e)
        {
            var process = (Process)sender;
            ProcessExit?.Invoke(process.Id, process.ExitCode);
        }
    }
}
