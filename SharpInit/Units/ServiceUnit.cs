using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace SharpInit.Units
{
    public class ServiceUnit : Unit
    {
        public new ServiceUnitFile File { get; set; }
        public override UnitFile GetUnitFile() => (UnitFile)File;

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
            
            ServiceManager.StartProcess(this, PrepareProcessStartInfo());
        }

        public override void Deactivate()
        {
            if (CurrentState == UnitState.Inactive || CurrentState == UnitState.Deactivating || CurrentState == UnitState.Failed)
                return;

            SetState(UnitState.Deactivating);
            
            if (ProcessInfo.PlatformSupportsSignaling)
                ServiceManager.ProcessesByUnit[this].ForEach(process => process.SendSignal(Mono.Unix.Native.Signum.SIGTERM));
            else
                ServiceManager.ProcessesByUnit[this].ForEach(process => process.Process.Kill());
        }

        private ProcessStartInfo PrepareProcessStartInfo()
        {
            ProcessStartInfo psi = new ProcessStartInfo();
            
            // let's set some sane defaults
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardInput = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.WorkingDirectory = File.WorkingDirectory;

            switch (File.ServiceType)
            {
                case ServiceType.Simple:
                    // there must be one execstart
                    // prefixes are not yet supported
                    if (File.ExecStart.Count != 1)
                        throw new InvalidOperationException("Service of type simple must have exactly one ExecStart line");

                    var line = File.ExecStart.Single();
                    var parts = UnitParser.SplitSpaceSeparatedValues(line);

                    psi.FileName = parts[0];
                    psi.Arguments = string.Join(" ", parts.Skip(1));

                    break;
                default:
                    throw new NotImplementedException();
            }

            return psi;
        }
    }
}
