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

        public int MainPid { get; set; }

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
        public override void SetUnitDescriptor(UnitDescriptor desc) 
        { 
            Descriptor = (ServiceUnitDescriptor)desc; 

            if (string.IsNullOrWhiteSpace(ParentSlice))
                ParentSlice = Descriptor.Slice; // This is sticky (if set once, can't be unset)

            base.SetUnitDescriptor(desc); 
        }

        public override IEnumerable<Dependency> GetDefaultDependencies()
        {
            foreach (var base_dep in base.GetDefaultDependencies())
                yield return base_dep;

            if (Descriptor.DefaultDependencies) 
            {
                yield return new RequirementDependency(left: UnitName, right: "sysinit.target", from: UnitName, type: RequirementDependencyType.Requires);
                yield return new RequirementDependency(left: UnitName, right: "basic.target", from: UnitName, type: RequirementDependencyType.Requires);
                yield return new RequirementDependency(left: UnitName, right: "shutdown.target", from: UnitName, type: RequirementDependencyType.Conflicts);
            }
        }

        public bool CanRestartNow()
        {
            if (StartupThrottle.IsThrottled())
            {
                Log.Warn($"Service {UnitName} restarting too quickly: suppressing restarts from now on.");
                RestartSuppressed = true;
            }
            
            return !RestartSuppressed;
        }

        private void HandleProcessExit(object sender, ServiceProcessExitEventArgs e)
        {
            SocketManager.UnignoreSocketsByUnit(this);

            switch(CurrentState)
            {
                case UnitState.Deactivating:
                    if (e.Process != null)
                        SetState(UnitState.Inactive, "Main process exited");
                    break;
                default:
                    if (!Descriptor.RemainAfterExit && e.Process != null)
                    {
                        // TODO: treat process exit differently based on service type
                        if (e.ExitCode != 0)
                        {
                            SetState(UnitState.Failed, $"Main process exited with code {e.ExitCode}");
                        }
                        else
                        {
                            SetState(UnitState.Inactive, "Main process exited");
                        }
                    }
                    else if (Descriptor.RemainAfterExit && e.Process != null)
                    {
                        SetState(CurrentState, $"Main process exited with code {e.ExitCode}");
                    }

                    var should_restart = false;

                    if (e.ExitCode == 0)
                        should_restart = Descriptor.Restart.HasFlag(RestartBehavior.CleanExit);
                    else
                        should_restart = Descriptor.Restart.HasFlag(RestartBehavior.UncleanExit);
                    
                    should_restart = should_restart && CanRestartNow();

                    if(should_restart)
                    {
                        ServiceManager.Runner.Register(GetRestartTransaction()).Enqueue();
                    }
                    break;
            }
        }

        protected Transaction GetRestartTransaction()
        {
            return new Transaction(
                new DelayTask(Descriptor.RestartSec),
                ServiceManager.Planner.CreateDeactivationTransaction(UnitName, $"{UnitName} is being restarted"),
                ServiceManager.Planner.CreateActivationTransaction(UnitName, $"{UnitName} is being restarted"));
        }

        private void HandleProcessStart(object sender, ServiceProcessStartEventArgs e)
        {
            // do nothing for now
        }

        internal override Transaction GetActivationTransaction()
        {
            var transaction = new UnitStateChangeTransaction(this, $"Activation transaction for {this.UnitName}");

            transaction.Add(new RecordUnitStartupAttemptTask(this));
            transaction.Add(new SetUnitStateTask(this, UnitState.Activating, UnitState.Inactive | UnitState.Failed));

            if (Descriptor.ServiceType != ServiceType.Oneshot && Descriptor.ExecStart?.Count != 1)
            {    
                Log.Error($"Service type \"{Descriptor.ServiceType}\" only supports one ExecStart value, {UnitName} has {Descriptor.ExecStart.Count}");
                SetState(UnitState.Failed, $"\"{Descriptor.ServiceType}\" service has more than one ExecStart");
                return null;       
            }
            
            transaction.Add(new AllocateSliceTask(this));

            if (!string.IsNullOrWhiteSpace(Descriptor.TtyPath))
            {
                transaction.Add(new ManipulateTtyTask(Descriptor));
            }
            
            foreach (var line in Descriptor.ExecStartPre)
                transaction.Add(new RunUnregisteredProcessTask(ServiceManager.ProcessHandler, ProcessStartInfo.FromCommandLine(line, this, Descriptor.TimeoutStartSec), Descriptor.TimeoutStartSec));

            switch (Descriptor.ServiceType)
            {
                case ServiceType.Idle:
                case ServiceType.Simple:
                    var simple_psi = ProcessStartInfo.FromCommandLine(Descriptor.ExecStart.Single(), this, Descriptor.TimeoutStartSec);
                    simple_psi.WaitUntilExec = false;

                    transaction.Add(new RunRegisteredProcessTask(simple_psi, this));
                    break;
                case ServiceType.Exec:
                    var exec_psi = ProcessStartInfo.FromCommandLine(Descriptor.ExecStart.Single(), this, Descriptor.TimeoutStartSec);
                    exec_psi.WaitUntilExec = true;

                    transaction.Add(new RunRegisteredProcessTask(exec_psi, this));
                    break;
                case ServiceType.Oneshot:
                    foreach (var line in Descriptor.ExecStart)
                    {
                        var oneshot_psi = ProcessStartInfo.FromCommandLine(Descriptor.ExecStart.Single(), this, Descriptor.TimeoutStartSec);
                        oneshot_psi.WaitUntilExec = true;
                        transaction.Add(new RunRegisteredProcessTask(oneshot_psi, this, true, (int)Descriptor.TimeoutStartSec.TotalMilliseconds));
                    }
                    break;
                case ServiceType.Forking:
                    var forking_psi = ProcessStartInfo.FromCommandLine(Descriptor.ExecStart.Single(), this, Descriptor.TimeoutStartSec);
                    forking_psi.WaitUntilExec = true;

                    transaction.Add(new RunRegisteredProcessTask(forking_psi, this, true, (int)Descriptor.TimeoutStartSec.TotalMilliseconds));
                    break;
                default:
                    Log.Error($"{UnitName} has unsupported service type {Descriptor.ServiceType}");
                    SetState(UnitState.Failed, $"Unsupported service type \"{Descriptor.ServiceType}\"");
                    break;
            }

            foreach (var line in Descriptor.ExecStartPost)
                transaction.Add(new RunUnregisteredProcessTask(ServiceManager.ProcessHandler, 
                ProcessStartInfo.FromCommandLine(line, this, Descriptor.TimeoutStartSec), Descriptor.TimeoutStartSec));

            if (Descriptor.ServiceType != ServiceType.Oneshot || Descriptor.RemainAfterExit)
                transaction.Add(new SetUnitStateTask(this, UnitState.Active, UnitState.Activating | UnitState.Active));
            
            transaction.Add(new UpdateUnitActivationTimeTask(this));
            transaction.OnFailure = transaction.OnTimeout = new HandleFailureTask(this);

            return transaction;
        }

        internal override Transaction GetDeactivationTransaction()
        {
            var transaction = new UnitStateChangeTransaction(this, $"Deactivation transaction for unit {UnitName}");

            transaction.Add(new SetUnitStateTask(this, UnitState.Deactivating));
            transaction.Add(new StopUnitProcessesTask(this));
            transaction.Add(new SetUnitStateTask(this, UnitState.Inactive, UnitState.Deactivating));

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

        public class HandleFailureTask : Task
        {
            public override string Type => "handle-service-failure";
            private ServiceUnit Unit { get; set; }

            public HandleFailureTask(ServiceUnit unit)
            {
                Unit = unit;
            }

            public override TaskResult Execute(TaskContext context)
            {
                if (!context.Has<TaskResult>("failure"))
                {   
                    Unit.SetState(UnitState.Failed);
                    return new TaskResult(this, ResultType.Success);
                }

                var result = context.Get<TaskResult>("failure");
                bool should_restart = false;

                if (result.Type.HasFlag(ResultType.Timeout))
                    if (Unit.Descriptor.Restart.HasFlag(RestartBehavior.Timeout))        
                        should_restart = true;
                
                if (result.Type.HasFlag(ResultType.Failure))
                    if (Unit.Descriptor.Restart.HasFlag(RestartBehavior.UncleanExit))
                        should_restart = true;

                bool should_restart_actual = should_restart && Unit.CanRestartNow();

                if (should_restart_actual)
                {
                    ServiceManager.Runner.Register(Unit.GetRestartTransaction()).Enqueue();
                }
                else
                {
                    if (should_restart)
                        Unit.SetState(UnitState.Failed, $"{result.Message} (restart throttled)");
                    else
                        Unit.SetState(UnitState.Failed, result.Message);
                    return new TaskResult(this, ResultType.Success);
                }

                return new TaskResult(this, ResultType.Success);
            }
        }
    }
}   
