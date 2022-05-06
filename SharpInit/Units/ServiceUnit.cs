using NLog;

using SharpInit.Platform;
using SharpInit.Platform.Unix;
using SharpInit.Tasks;

using Mono.Unix.Native;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;

namespace SharpInit.Units
{
    public class ServiceUnit : Unit
    {
        static Dictionary<(string, UnitStateChangeType), (string, string)> CustomStatusMessages = new Dictionary<(string, UnitStateChangeType), (string, string)>();

        public override Dictionary<(string, UnitStateChangeType), (string, string)> StatusMessages => CustomStatusMessages;
        Logger Log = LogManager.GetCurrentClassLogger();
        
        public Socket NotifySocket { get; set; }
        public NotifyClient NotifyClient { get; set; }

        public new ServiceUnitDescriptor Descriptor { get; set; }

        public List<FileDescriptor> StoredFileDescriptors { get; set; } = new();

        public int COMMAND_TIMEOUT = 5000;

        public ServiceUnit(string name, ServiceUnitDescriptor desc) :
            base(name, desc)
        {
            ProcessStart += HandleProcessStart;
            ProcessExit += HandleProcessExit;
            BusNameReleased += HandleBusNameReleased;
        }

        public ServiceUnit() : base()
        {
            ProcessStart += HandleProcessStart;
            ProcessExit += HandleProcessExit;
            BusNameReleased += HandleBusNameReleased;
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
                yield return new OrderingDependency(left: UnitName, right: "sysinit.target", from: UnitName,
                    type: OrderingDependencyType.After);
                yield return new OrderingDependency(left: UnitName, right: "basic.target", from: UnitName,
                    type: OrderingDependencyType.After);

                yield return new RequirementDependency(left: UnitName, right: "basic.target", from: UnitName, type: RequirementDependencyType.Requires);
                yield return new RequirementDependency(left: UnitName, right: "shutdown.target", from: UnitName, type: RequirementDependencyType.Conflicts);
            }
        }

        public void HandleNotifyMessage(NotifyMessage message)
        {
            var contents = message.Contents;
            var parts = contents.Split('=');
            var key = parts[0].ToUpperInvariant();
            var value = string.Join('=', parts.Skip(1));

            Log.Debug($"Received sd_notify message for {UnitName} from pid {message.Credentials.pid}: {message}");

            switch (key)
            {
                case "READY":
                    if (CurrentState == UnitState.Reloading)
                    {
                        SetState(UnitState.Active, "Unit is ready (sd_notify)");
                    }
                    break;
                case "MAINPID":
                    if (int.TryParse(value, out int pid))
                        MainProcessId = pid;
                    break;
                case "STATUS":
                    Status = value;
                    break;
                case "RELOADING":
                    SetState(UnitState.Reloading, "Unit is reloading (sd_notify)");
                    break;
                case "STOPPING":
                    Log.Info($"Unit {this.UnitName} is stopping (sd_notify)");
                    break;
                case "ERRNO":
                    break;
                case "BUSERROR":
                    break;
                case "WATCHDOG":
                    break;
                case "WATCHDOG_USEC":
                    break;
                case "FDSTORE":
                    // unset message.FileDescriptors to prevent later closure
                    StoredFileDescriptors.AddRange(message.FileDescriptors);
                    message.FileDescriptors = new FileDescriptor[0];
                    break;
                case "FDSTOREREMOVE":
                    break;
                case "FDNAME":
                    break;
                case "FDPOLL":
                    break;
                case "BARRIER":
                    break;
                default:
                    if (!key.StartsWith("X-"))
                        Log.Warn($"Received unrecognized sd_notify message {message}");
                    break;
            }
            
            if (message.FileDescriptors != null)
                foreach (var fd in message.FileDescriptors)
                    Syscall.close(fd.Number);
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
        
        private void HandleBusNameReleased(object sender, BusNameReleasedEventArgs e)
        {
            switch(CurrentState)
            {
                case UnitState.Deactivating:
                    SetState(UnitState.Inactive, "Bus name released");
                    break;
                default:
                    if (!Descriptor.RemainAfterExit)
                    {
                        SetState(UnitState.Inactive, "Bus name released");
                    }
                    else if (Descriptor.RemainAfterExit)
                    {
                        SetState(CurrentState, $"Bus name released");
                    }
                    break;
            }

            HandleUnitStopped(0);
        }

        private void HandleProcessExit(object sender, ServiceProcessExitEventArgs e)
        {
            if (Descriptor.ExitType == ExitType.CGroup)
            {
                if (CGroup != null)
                {
                    CGroup.Update();
                    if (CGroup.ChildProcesses.Any())
                        return;
                }
            }
            else
            {
                if (e.Process?.Id == MainProcessId)
                {
                    MainProcessId = -1;
                }
                else
                {
                    return;
                }
            }
            
            SocketManager.UnignoreSocketsByUnit(this);
            
            if (Descriptor.ServiceType == ServiceType.Dbus)
            {
                return;
            }

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
                    break;
            }
            
            HandleUnitStopped(e.ExitCode);
        }

        private void HandleUnitStopped(int exit_code)
        {
            var should_restart = false;

            if (exit_code == 0)
                should_restart = Descriptor.Restart.HasFlag(RestartBehavior.CleanExit);
            else
                should_restart = Descriptor.Restart.HasFlag(RestartBehavior.UncleanExit);

            should_restart = should_restart && CanRestartNow();

            if(should_restart)
            {
                ServiceManager.Runner.Register(GetRestartTransaction()).Enqueue();
            }
            else
            {
                ServiceManager.Runner.Register(LateBoundUnitActivationTask.CreateDeactivationTransaction(this));
            }
        }

        protected Transaction GetRestartTransaction()
        {
            return new Transaction(
                new DelayTask(Descriptor.RestartSec),
                LateBoundUnitActivationTask.CreateDeactivationTransaction(UnitName, $"{UnitName} is being restarted"),
                LateBoundUnitActivationTask.CreateActivationTransaction(UnitName, $"{UnitName} is being restarted"));
        }

        private void HandleProcessStart(object sender, ServiceProcessStartEventArgs e)
        {
            // do nothing for now
        }

        internal override Transaction GetActivationTransaction()
        {
            var transaction = new UnitStateChangeTransaction(this, UnitStateChangeType.Activation);

            /*
                If unit is active, end transaction with success.
                If unit is startup throttled, fail unit activation.
                  Record a unit startup attempt. 
                Indicate that unit is activating. Clear previous failure or inactivity.
                Allocate a slice for the service. 
                Execute ExecCondition= directives serially.
                  For each ExecCondition=, run the command line.
                    If exit_code > 0, skip unit activation and the rest of any commands, but run ExecStopPost=
                    If exit_code > 254, fail unit activation and the rest of any commands, but run ExecStopPost=
                If any TTY= settings, manipulate the mentioned tty.
                Execute ExecStartPre= directives serially.
                  For each ExecStartPre=, run the command line.
                    On failure, fail unit activation and the rest of any commands, but run ExecStopPost=
                Execute ExecStart= directives serially.
                  For each ExecStart=, run the command line.
                    On failure, fail unit activation and the rest of any commands, but run ExecStopPost=
                If service type is dbus or notify, wait for the appropriate notification.
                Execute ExecStartPost= directives serially.
                  For each ExecStartPost=, run the command line.
                    On failure, fail unit activation and the rest of any commands, but run ExecStopPost=
                If unit is still 'activating', indicate that unit is active.

                If unit activation has failed, check whether unit can/should be restarted, including rate limiting.
                  If it should, enqueue an _automated_ restart.
                  If not, mark the unit as failed.
            */

            /*
                // when unit enters failed

            */

            /*
                Upon receiving a $MAINPID exit, dbus name release, 
                Set unit state to "deactivating."

            */

            transaction.Precheck = this.StopIf(UnitState.Active);

            transaction.Add(new SetUnitStateTask(this, UnitState.Inactive));
            transaction.Add(new CheckUnitConditionsTask(this));

            if (Descriptor.ServiceType != ServiceType.Oneshot && Descriptor.ExecStart?.Count != 1)
            {    
                Log.Warn($"Service type \"{Descriptor.ServiceType}\" only supports one ExecStart value, {UnitName} has {Descriptor.ExecStart.Count}");
                //SetState(UnitState.Failed, $"\"{Descriptor.ServiceType}\" service has more than one ExecStart");
                //return null;       
            }
            
            transaction.Add(new RecordUnitStartupAttemptTask(this));
            transaction.Add(new AllocateSliceTask(this));
            
            if (Descriptor.ServiceType == ServiceType.Notify || Descriptor.NotifyAccess != NotifyAccess.None)
            {
                transaction.Add(new CreateNotifySocketTask(this));   
            }
            
            transaction.Add(new RetrieveStoredFileDescriptorsTask(this));
            
            foreach (var line in Descriptor.ExecCondition)
            {
                transaction.Add(new RunUnregisteredProcessTask(ServiceManager.ProcessHandler, ProcessStartInfo.FromCommandLine(line, this, Descriptor.TimeoutStartSec), Descriptor.TimeoutStartSec));
                transaction.Add(new CheckProcessExitCodeTask(RunUnregisteredProcessTask.LastProcessKey, exit_code => {
                    if (exit_code == 0)
                        return ResultType.Success;
                    
                    if (exit_code < 255)
                        return ResultType.Success | ResultType.Skipped | ResultType.StopExecution;
                    
                    return ResultType.Failure;
                }));
            }
            
            transaction.Add(new SetUnitStateTask(this, UnitState.Activating, UnitState.Inactive | UnitState.Failed));

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
                        var oneshot_psi = ProcessStartInfo.FromCommandLine(line, this, Descriptor.TimeoutStartSec);
                        oneshot_psi.WaitUntilExec = true;
                        transaction.Add(new RunRegisteredProcessTask(oneshot_psi, this, true, (int)Descriptor.TimeoutStartSec.TotalMilliseconds));
                    }
                    break;
                case ServiceType.Forking:
                    var forking_psi = ProcessStartInfo.FromCommandLine(Descriptor.ExecStart.Single(), this, Descriptor.TimeoutStartSec);
                    forking_psi.WaitUntilExec = true;

                    transaction.Add(new RunRegisteredProcessTask(forking_psi, this, true, (int)Descriptor.TimeoutStartSec.TotalMilliseconds));
                    break;
                case ServiceType.Notify:
                    var notify_psi = ProcessStartInfo.FromCommandLine(Descriptor.ExecStart.Single(), this, Descriptor.TimeoutStartSec);
                    notify_psi.WaitUntilExec = true;

                    transaction.Add(new RunRegisteredProcessTask(notify_psi, this, false, (int)Descriptor.TimeoutStartSec.TotalMilliseconds));
                    transaction.Add(new WaitForNotifySocketTask(this, (int)Descriptor.TimeoutStartSec.TotalMilliseconds));
                    break;
                case ServiceType.Dbus:
                    var dbus_psi = ProcessStartInfo.FromCommandLine(Descriptor.ExecStart.Single(), this, Descriptor.TimeoutStartSec);
                    dbus_psi.WaitUntilExec = true;

                    transaction.Add(new RunRegisteredProcessTask(dbus_psi, this, true, (int)Descriptor.TimeoutStartSec.TotalMilliseconds));
                    transaction.Add(new WaitForDBusName(Descriptor.BusName, (int)Descriptor.TimeoutStartSec.TotalMilliseconds));
                    break;
                default:
                    Log.Error($"{UnitName} has unsupported service type {Descriptor.ServiceType}");
                    SetState(UnitState.Failed, $"Unsupported service type \"{Descriptor.ServiceType}\"");
                    break;
            }
            
            transaction.Add(new ForgetStoredFileDescriptorsTask(this));

            foreach (var line in Descriptor.ExecStartPost)
                transaction.Add(new RunUnregisteredProcessTask(ServiceManager.ProcessHandler, 
                ProcessStartInfo.FromCommandLine(line, this, Descriptor.TimeoutStartSec), Descriptor.TimeoutStartSec));

            if (Descriptor.ServiceType != ServiceType.Oneshot || Descriptor.RemainAfterExit)
                transaction.Add(new SetUnitStateTask(this, UnitState.Active, UnitState.Activating | UnitState.Active, fail_silently: true));
            
            transaction.Add(new SetMainPidTask(this, transaction.Tasks.OfType<RunRegisteredProcessTask>().FirstOrDefault()));
            transaction.Add(new UpdateUnitActivationTimeTask(this));
            transaction.OnFailure = transaction.OnTimeout = transaction.OnSkipped = new HandleFailureTask(this);

            return transaction;
        }

        internal override Transaction GetDeactivationTransaction()
        {
            var transaction = new UnitStateChangeTransaction(this, UnitStateChangeType.Deactivation);

            transaction.Precheck = this.StopIf(UnitState.Inactive);
            
            var exec_stop_tx = new Transaction();
            exec_stop_tx.ErrorHandlingMode = TransactionErrorHandlingMode.Ignore;
            exec_stop_tx.Add(this.StopUnless(UnitState.Active));

            foreach (var line in Descriptor.ExecStop)
            {
                exec_stop_tx.Add(new RunUnregisteredProcessTask(ServiceManager.ProcessHandler, 
                    ProcessStartInfo.FromCommandLine(line, this, Descriptor.TimeoutStopSec), Descriptor.TimeoutStopSec));
            }

            transaction.Add(exec_stop_tx);
            transaction.Add(new SetUnitStateTask(this, UnitState.Deactivating));
            transaction.Add(new StopUnitProcessesTask(this));

            var exec_stop_post_tx = new Transaction();
            exec_stop_post_tx.ErrorHandlingMode = TransactionErrorHandlingMode.Ignore;

            foreach (var line in Descriptor.ExecStopPost)
            {
                exec_stop_post_tx.Add(new RunUnregisteredProcessTask(ServiceManager.ProcessHandler, 
                    ProcessStartInfo.FromCommandLine(line, this, Descriptor.TimeoutStopSec), Descriptor.TimeoutStopSec));
            }

            transaction.Add(exec_stop_post_tx);
            transaction.Add(new SetUnitStateTask(this, UnitState.Inactive, UnitState.Deactivating));

            transaction.OnFailure = new SetUnitStateTask(this, UnitState.Failed);

            return transaction;
        }

        public override Transaction GetReloadTransaction()
        {
            var transaction = new UnitStateChangeTransaction(this, UnitStateChangeType.Unknown);
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
                bool should_restart = false;
                var next_state = UnitState.Failed;
                var message = "";

                if (!context.Has<TaskResult>("failure"))
                {   
                    next_state = UnitState.Inactive;
                }
                else
                {
                    var result = context.Get<TaskResult>("failure");
                    message = result.Message;

                    if (result.Type.HasFlag(ResultType.Ignorable) || 
                        result.Type.HasFlag(ResultType.Skipped))
                    {
                        next_state = UnitState.Inactive;
                    }

                    if (result.Type.HasFlag(ResultType.Timeout))
                        if (Unit.Descriptor.Restart.HasFlag(RestartBehavior.Timeout))        
                            should_restart = true;
                    
                    if (result.Type.HasFlag(ResultType.Failure) && !result.Type.HasFlag(ResultType.Ignorable))
                        if (Unit.Descriptor.Restart.HasFlag(RestartBehavior.UncleanExit))
                            should_restart = true;
                }

                bool should_restart_actual = should_restart && Unit.CanRestartNow();

                if (should_restart_actual)
                {
                    ServiceManager.Runner.Register(Unit.GetRestartTransaction()).Enqueue();
                }
                else
                {
                    if (should_restart)
                        Unit.SetState(next_state, $"{message} (restart throttled)");
                    else
                    {
                        Unit.SetState(next_state, message);

                        var deactivation = Unit.GetDeactivationTransaction() as UnitStateChangeTransaction;
                        deactivation.Precheck = null;
                        deactivation.Add(new SetUnitStateTask(Unit, next_state, UnitState.Any, message));
                        ServiceManager.Runner.Register(deactivation).Enqueue();
                    }
                    return new TaskResult(this, ResultType.Success);
                }

                return new TaskResult(this, ResultType.Success);
            }
        }
    }
}   
