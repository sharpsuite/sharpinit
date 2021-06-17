using NLog;
using SharpInit.Platform;
using SharpInit.Tasks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace SharpInit.Units
{
    public class ServiceUnit : Unit
    {
        Logger Log = LogManager.GetCurrentClassLogger();

        public new ServiceUnitDescriptor Descriptor { get; set; }

        public int COMMAND_TIMEOUT = 5000;

        public ServiceUnit(string name, ServiceUnitDescriptor desc) :
            base(name, desc)
        {
            ProcessStart += HandleProcessStart;
            ProcessExit += HandleProcessExit;
        }

        public ServiceUnit() : base()
        {
            ProcessStart += HandleProcessStart;
            ProcessExit += HandleProcessExit;
        }

        public override UnitDescriptor GetUnitDescriptor() => Descriptor;
        public override void SetUnitDescriptor(UnitDescriptor desc) => Descriptor = (ServiceUnitDescriptor)desc;

        private void HandleProcessExit(Unit unit, ProcessInfo info, int code)
        {
            SocketManager.UnignoreSocketsByUnit(this);
            
            switch(CurrentState)
            {
                case UnitState.Deactivating:
                    SetState(UnitState.Inactive, "Main process exited");
                    break;
                default:
                    // TODO: treat process exit differently based on service type
                    if (code != 0)
                    {
                        SetState(UnitState.Failed, $"Main process exited with code {code}");
                    }
                    else
                    {
                        SetState(UnitState.Inactive, "Main process exited");
                    }

                    var should_restart = false;

                    if (code == 0)
                        should_restart =
                            Descriptor.Restart == RestartBehavior.Always ||
                            Descriptor.Restart == RestartBehavior.OnSuccess;
                    else
                        should_restart =
                            Descriptor.Restart == RestartBehavior.Always ||
                            Descriptor.Restart == RestartBehavior.OnFailure ||
                            Descriptor.Restart == RestartBehavior.OnAbnormal;

                    if(should_restart)
                    {
                        var restart_transaction = new Transaction(
                            new DelayTask(Descriptor.RestartSec),
                            UnitRegistry.CreateDeactivationTransaction(UnitName),
                            UnitRegistry.CreateActivationTransaction(UnitName));

                        // TODO: un-hack this
                        new Thread((ThreadStart)delegate { restart_transaction.Execute(); }).Start();
                    }
                    break;
            }
        }

        private void HandleProcessStart(Unit unit, ProcessInfo info)
        {
            // do nothing for now
        }

        internal override Transaction GetActivationTransaction()
        {
            var transaction = new UnitStateChangeTransaction(this, $"Activation transaction for {this.UnitName}");
            transaction.Add(new SetUnitStateTask(this, UnitState.Activating, UnitState.Inactive | UnitState.Failed));

            switch (Descriptor.ServiceType)
            {
                case ServiceType.Simple:
                    if (Descriptor.ExecStart == null)
                    {
                        Log.Error($"Unit {UnitName} has no ExecStart directives.");
                        SetState(UnitState.Failed, "Unit has no ExecStart directives");
                        return null;
                    }

                    if (Descriptor.ExecStart.Count != 1)
                    {
                        Log.Error($"Service type \"simple\" only supports one ExecStart value, {UnitName} has {Descriptor.ExecStart.Count}");
                        SetState(UnitState.Failed, "\"simple\" service has more than one ExecStart");
                        return null;
                    }

                    if (Descriptor.ExecStartPre.Any())
                    {
                        foreach (var line in Descriptor.ExecStartPre)
                            transaction.Add(new RunUnregisteredProcessTask(ServiceManager.ProcessHandler, ProcessStartInfo.FromCommandLine(line, this, Descriptor.TimeoutStartSec), Descriptor.TimeoutStartSec));
                    }

                    transaction.Add(new RunRegisteredProcessTask(ProcessStartInfo.FromCommandLine(Descriptor.ExecStart.Single(), this, Descriptor.TimeoutStartSec), this));

                    if (Descriptor.ExecStartPost.Any())
                    {
                        foreach (var line in Descriptor.ExecStartPost)
                            transaction.Add(new RunUnregisteredProcessTask(ServiceManager.ProcessHandler, 
                            ProcessStartInfo.FromCommandLine(line, this, Descriptor.TimeoutStartSec), Descriptor.TimeoutStartSec));
                    }
                    break;
                default:
                    Log.Error($"Only the \"simple\" service type is supported for now, {UnitName} has type {Descriptor.ServiceType}");
                    SetState(UnitState.Failed, $"Unsupported service type \"{Descriptor.ServiceType}\"");
                    break;
            }

            transaction.Add(new SetUnitStateTask(this, UnitState.Active, UnitState.Activating));
            transaction.Add(new UpdateUnitActivationTimeTask(this));

            transaction.OnFailure = new SetUnitStateTask(this, UnitState.Failed);

            return transaction;
        }

        internal override Transaction GetDeactivationTransaction()
        {
            var transaction = new UnitStateChangeTransaction(this, $"Deactivation transaction for unit {UnitName}");

            transaction.Add(new SetUnitStateTask(this, UnitState.Deactivating, UnitState.Active));
            transaction.Add(new StopUnitProcessesTask(this));

            transaction.OnFailure = new SetUnitStateTask(this, UnitState.Failed);

            return transaction;
        }

        public override Transaction GetReloadTransaction()
        {
            var transaction = new UnitStateChangeTransaction(this, $"Reload transaction for unit {UnitName}");
            transaction.Add(new SetUnitStateTask(this, UnitState.Reloading, UnitState.Active));

            var working_dir = Descriptor.WorkingDirectory;
            var user = (Descriptor.Group == null && Descriptor.User == null ? null : PlatformUtilities.GetImplementation<IUserIdentifier>(Descriptor.Group, Descriptor.User));

            if (!Descriptor.ExecReload.Any())
            {
                throw new Exception($"Unit {UnitName} has no ExecReload directives.");
            }
            
            foreach(var reload_cmd in Descriptor.ExecReload)
            {
                transaction.Add(new RunUnregisteredProcessTask(ServiceManager.ProcessHandler, 
                    ProcessStartInfo.FromCommandLine(reload_cmd, this), 5000));
            }

            transaction.Add(new SetUnitStateTask(this, UnitState.Active, UnitState.Reloading));

            return transaction;
        }
    }
}   
