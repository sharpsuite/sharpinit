using NLog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace SharpInit.Units
{
    public class ServiceUnit : Unit
    {
        Logger Log = LogManager.GetCurrentClassLogger();

        public new ServiceUnitFile File { get; set; }
        public override UnitFile GetUnitFile() => (UnitFile)File;

        public int COMMAND_TIMEOUT = 5000;

        public ServiceUnit(string path) : base(path)
        {
            ProcessStart += HandleProcessStart;
            ProcessExit += HandleProcessExit;
        }

        private void HandleProcessExit(Unit unit, ProcessInfo info, int code)
        {
            switch(CurrentState)
            {
                case UnitState.Deactivating:
                    SetState(UnitState.Inactive);
                    break;
                default:
                    // TODO: treat process exit differently based on service type
                    if (code != 0)
                        SetState(UnitState.Failed);
                    else
                        SetState(UnitState.Inactive);
                    break;
            }
        }

        private void HandleProcessStart(Unit unit, ProcessInfo info)
        {
            SetState(UnitState.Active);
        }

        public override void LoadUnitFile(string path)
        {
            File = UnitParser.Parse<ServiceUnitFile>(path);
        }

        public override void Activate()
        {
            if (CurrentState == UnitState.Active || CurrentState == UnitState.Activating || CurrentState == UnitState.Failed)
                return;

            SetState(UnitState.Activating);

            switch (File.ServiceType)
            {
                case ServiceType.Simple:
                    if (File.ExecStart == null)
                    {
                        Log.Error($"Unit {UnitName} has no ExecStart directives.");
                        SetState(UnitState.Failed);
                        break;
                    }

                    if (File.ExecStart.Count != 1)
                    {
                        Log.Error($"Service type \"simple\" only supports one ExecStart value, {UnitName} has {File.ExecStart.Count}");
                        SetState(UnitState.Failed);
                        break;
                    }

                    if (File.ExecStartPre.Any())
                        File.ExecStartPre.ForEach(pre => Process.Start(PrepareProcessStartInfoFromCommandLine(pre)).WaitForExit(5000)); // these are not tracked by ServiceManager

                    ServiceManager.StartProcess(this, PrepareProcessStartInfoFromCommandLine(File.ExecStart.Single()));

                    if (File.ExecStartPost.Any())
                        File.ExecStartPost.ForEach(post => Process.Start(PrepareProcessStartInfoFromCommandLine(post)).WaitForExit(5000)); // these are not tracked by ServiceManager
                    break;
                default:
                    Log.Error($"Only the \"simple\" service type is supported for now, {UnitName} has type {File.ServiceType}");
                    SetState(UnitState.Failed);
                    break;
            }
        }

        public override void Deactivate()
        {
            if (CurrentState == UnitState.Inactive || CurrentState == UnitState.Deactivating || CurrentState == UnitState.Failed)
                return;

            SetState(UnitState.Deactivating);
            
            if (ProcessInfo.PlatformSupportsSignaling)
                ServiceManager.ProcessesByUnit[this].ForEach(process => process.SendSignal(Mono.Unix.Native.Signum.SIGTERM));
            else
                ServiceManager.ProcessesByUnit[this].ForEach(process => { try { process.Process.Kill(); } catch { } });
        }

        public override void Reload()
        {
            if (CurrentState == UnitState.Inactive || CurrentState == UnitState.Deactivating || CurrentState == UnitState.Failed)
                return;

            if(!File.ExecReload.Any())
            {
                throw new Exception($"Unit {UnitName} has no ExecReload directives.");
            }
            
            foreach(var reload_cmd in File.ExecReload)
            {
                var process = Process.Start(PrepareProcessStartInfoFromCommandLine(reload_cmd));
                var result = process.WaitForExit(5000);

                if(!result)
                {
                    process.Kill();
                    Deactivate();
                    SetState(UnitState.Failed);
                    throw new TimeoutException($"Unit {UnitName} timed out while reloading (exceeded {COMMAND_TIMEOUT} milliseconds)");
                }
            }
        }

        private ProcessStartInfo PrepareProcessStartInfoFromCommandLine(string cmdline)
        {
            ProcessStartInfo psi = new ProcessStartInfo();
            
            // let's set some sane defaults
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardInput = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.WorkingDirectory = File.WorkingDirectory;

            var parts = UnitParser.SplitSpaceSeparatedValues(cmdline);

            psi.FileName = parts[0];
            psi.Arguments = string.Join(" ", parts.Skip(1));
            
            return psi;
        }
    }
}
