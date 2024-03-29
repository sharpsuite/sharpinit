﻿using NLog;
using SharpInit.Platform;
using SharpInit.Tasks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.IO;

namespace SharpInit.Units
{
    public class SocketUnit : Unit
    {
        static Dictionary<(string, UnitStateChangeType), (string, string)> CustomStatusMessages = new Dictionary<(string, UnitStateChangeType), (string, string)>()
        {
            {("success", UnitStateChangeType.Activation), ("  OK  ", "Listening on {0}.")},
            {("success", UnitStateChangeType.Deactivation), ("  OK  ", "Closed {0}.")},

            {("failure", UnitStateChangeType.Activation), ("FAILED", "Failed to listen on {0}.")}
        };

        public override Dictionary<(string, UnitStateChangeType), (string, string)> StatusMessages => CustomStatusMessages;
        Logger Log = LogManager.GetCurrentClassLogger();

        public new SocketUnitDescriptor Descriptor { get; set; }

        public event OnSocketActivation SocketActivated;

        public SocketUnit(string name, SocketUnitDescriptor descriptor)
            : base(name, descriptor)
        {
            SocketActivated += HandleSocketActivated;
        }

        public override IEnumerable<Dependency> GetDefaultDependencies()
        {
            foreach (var base_dep in base.GetDefaultDependencies())
                yield return base_dep;

            if (Descriptor.DefaultDependencies)
            {
                yield return new RequirementDependency(left: UnitName, right: "sysinit.target", from: UnitName, type: RequirementDependencyType.Requires);
                yield return new RequirementDependency(left: UnitName, right: "shutdown.target", from: UnitName, type: RequirementDependencyType.Conflicts);
                yield return new OrderingDependency(left: "sockets.target", right: UnitName, from: UnitName, type: OrderingDependencyType.After);

                foreach (var wanted in Descriptor.Wants.Concat(Descriptor.Requires))
                    if (Registry.GetUnit(wanted)?.Descriptor?.DefaultDependencies ?? false != false)
                        yield return new OrderingDependency(left: UnitName, right: wanted, from: UnitName, type: OrderingDependencyType.After);
            }
        }

        internal void RaiseSocketActivated(SocketWrapper wrapper) => SocketActivated?.Invoke(wrapper);

        private void HandleSocketActivated(SocketWrapper wrapper)
        {
            var socket = wrapper.Socket;

            var activation_target = Descriptor.Service ?? Path.GetFileNameWithoutExtension(this.UnitName) + ".service";
            var target_unit = Registry.GetUnit(activation_target);

            if (target_unit == null)
            {
                Log.Warn($"Could not find activation target \"{activation_target}\" for socket \"{this.UnitName}\"");
                return;
            }

            if (Descriptor.Accept)
            {
                socket = socket.Accept();
            }
            else
            {
                if (target_unit.CurrentState == UnitState.Active || target_unit.CurrentState == UnitState.Activating)
                {
                    //Log.Debug($"Returning for already-active socket target \"{target_unit}\"");
                    return;
                }

                if (target_unit.CurrentSocketActivation != null &&
                    (target_unit.CurrentSocketActivation.State != TaskExecutionState.Finished &&
                     target_unit.CurrentSocketActivation.State != TaskExecutionState.Aborted))
                {
                    return;
                }
            }

            lock (socket)
            {
                var transaction = new UnitStateChangeTransaction(this, UnitStateChangeType.Activation,
                    $"Socket activation for {target_unit.UnitName}");
                transaction.Add(new AlterTransactionContextTask("state_change_reason",
                    $"Socket activation from {UnitName}"));

                var file_descriptors = new List<FileDescriptor>();

                if (!Descriptor.Accept)
                {
                    file_descriptors.AddRange(SocketManager.GetSocketsByUnit(this).Select(wrapper =>
                        new FileDescriptor(wrapper.Socket.Handle.ToInt32(),
                            Descriptor.FileDescriptorName ?? this.UnitName, -1)));
                }
                else
                {
                    file_descriptors.Add(new FileDescriptor(socket.Handle.ToInt32(),
                        Descriptor.FileDescriptorName ?? this.UnitName, -1));
                }

                transaction.Add(new AlterTransactionContextTask("socket.fds", file_descriptors));
                transaction.Add(target_unit.GetActivationTransaction());

                if (!Descriptor.Accept)
                {
                    transaction.Add(new IgnoreSocketsTask(this, target_unit));
                }

                target_unit.CurrentSocketActivation = ServiceManager.Runner.Register(transaction).Enqueue();
            }
        }

        public override UnitDescriptor GetUnitDescriptor() => Descriptor;
        public override void SetUnitDescriptor(UnitDescriptor desc) => Descriptor = (SocketUnitDescriptor)desc;

        internal override Transaction GetActivationTransaction()
        {
            var transaction = new UnitStateChangeTransaction(this, UnitStateChangeType.Activation);
            transaction.Precheck = this.StopIf(UnitState.Active);

            transaction.Add(new CheckUnitConditionsTask(this));
            transaction.Add(new RecordUnitStartupAttemptTask(this));
            transaction.Add(new SetUnitStateTask(this, UnitState.Activating, UnitState.Inactive | UnitState.Failed));

            if (Descriptor.ExecStartPre.Any())
            {
                foreach (var line in Descriptor.ExecStartPre)
                    transaction.Add(new RunUnregisteredProcessTask(ServiceManager.ProcessHandler, ProcessStartInfo.FromCommandLine(line, this, Descriptor.TimeoutSec), Descriptor.TimeoutSec));
            }

            transaction.Add(new CreateRegisteredSocketTask(this));
            transaction.Add(new SetUnitStateTask(this, UnitState.Active, UnitState.Activating));

            if (Descriptor.ExecStartPre.Any())
            {
                foreach (var line in Descriptor.ExecStartPost)
                    transaction.Add(new RunUnregisteredProcessTask(ServiceManager.ProcessHandler, ProcessStartInfo.FromCommandLine(line, this, Descriptor.TimeoutSec), Descriptor.TimeoutSec));
            }

            transaction.Add(new UpdateUnitActivationTimeTask(this));

            transaction.OnFailure = transaction.OnTimeout = new Transaction(
                new SetUnitStateTask(this, UnitState.Failed),
                new StopUnitSocketsTask(this));

            return transaction;
        }

        internal override Transaction GetDeactivationTransaction()
        {
            var transaction = new UnitStateChangeTransaction(this, UnitStateChangeType.Deactivation);
            transaction.Precheck = this.StopIf(UnitState.Inactive);

            transaction.Add(new SetUnitStateTask(this, UnitState.Deactivating));

            if (Descriptor.ExecStopPre.Any())
            {
                foreach (var line in Descriptor.ExecStopPre)
                    transaction.Add(new RunUnregisteredProcessTask(ServiceManager.ProcessHandler, ProcessStartInfo.FromCommandLine(line, this, Descriptor.TimeoutSec), Descriptor.TimeoutSec));
            }

            transaction.Add(new StopUnitSocketsTask(this));

            if (Descriptor.ExecStopPost.Any())
            {
                foreach (var line in Descriptor.ExecStopPost)
                    transaction.Add(new RunUnregisteredProcessTask(ServiceManager.ProcessHandler, ProcessStartInfo.FromCommandLine(line, this, Descriptor.TimeoutSec), Descriptor.TimeoutSec));
            }

            transaction.Add(new SetUnitStateTask(this, UnitState.Inactive, UnitState.Deactivating));

            return transaction;
        }

        public override Transaction GetReloadTransaction()
        {
            return new Transaction();
        }
    }
}
