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

        public ProcessInfo Start(ProcessStartInfo psi)
        {
            var path = psi.Path;

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

            var net_psi = new System.Diagnostics.ProcessStartInfo(path, string.Join(" ", psi.Arguments));

            if (Directory.Exists(psi.WorkingDirectory))
                net_psi.WorkingDirectory = psi.WorkingDirectory;

            net_psi.UserName = psi.User.Username;
            net_psi.Domain = psi.User.Group;

            net_psi.RedirectStandardOutput = true;
            net_psi.RedirectStandardError = true;
            net_psi.RedirectStandardInput = true;

            net_psi.UseShellExecute = false;

            var process = Process.Start(net_psi);
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
