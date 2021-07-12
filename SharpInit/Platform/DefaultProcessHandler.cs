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
    /// <summary>
    /// Process handler that uses the <c>System.Diagnostic.Process</c> API for managing processes.
    /// </summary>
    [SupportedOn("generic")]
    public class DefaultProcessHandler : IProcessHandler
    {
        public event OnProcessExit ProcessExit;

        public ServiceManager ServiceManager { get; set; }

        public DefaultProcessHandler()
        {

        }

        public async System.Threading.Tasks.Task<ProcessInfo> StartAsync(ProcessStartInfo psi) => Start(psi);

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

            if (psi.User != null)
            {
                net_psi.UserName = psi.User.Username;
                net_psi.Domain = psi.User.Group;
            }

            net_psi.RedirectStandardOutput = true;
            net_psi.RedirectStandardError = true;
            net_psi.RedirectStandardInput = true;

            net_psi.UseShellExecute = false;

            foreach (var env_var in psi.Environment)
            {
                net_psi.Environment[env_var.Key] = env_var.Value;
            }

            var process = Process.Start(net_psi);
            process.Exited += HandleProcessExit;
            process.EnableRaisingEvents = true;

            process.StandardInput.Close();
            process.StandardOutput.Close();
            process.StandardError.Close();

            return new ProcessInfo(process);
        }

        private void HandleProcessExit(object sender, EventArgs e)
        {
            var process = (Process)sender;
            ProcessExit?.Invoke(process.Id, process.ExitCode);
        }
    }
}
