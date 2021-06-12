using NLog;
using SharpInit.Tasks;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.IO;

namespace SharpInit.Units
{
    public class SocketUnit : Unit
    {
        Logger Log = LogManager.GetCurrentClassLogger();

        public new SocketUnitDescriptor Descriptor { get; set; }

        public event OnSocketActivation SocketActivated;

        public SocketUnit(string name, SocketUnitDescriptor descriptor)
            : base(name, descriptor)
        {
            SocketActivated += HandleSocketActivated;
        }

        internal void RaiseSocketActivated(SocketWrapper wrapper) => SocketActivated?.Invoke(wrapper);

        private void HandleSocketActivated(SocketWrapper wrapper)
        {
            var socket = wrapper.Socket;

            var activation_target = Descriptor.Service ?? Path.GetFileNameWithoutExtension(this.UnitName) + ".service";
            var target_unit = UnitRegistry.GetUnit(activation_target);

            if (target_unit == null)
            {
                Log.Warn($"Could not find activation target \"{target_unit}\" for socket \"{this.UnitName}\"");
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
                    Log.Debug($"Returning for already-active socket target \"{target_unit}\"");
                    return;
                }
            }

            var transaction = new Transaction($"Socket activation for {this.UnitName}");

            var file_descriptors = SocketManager.GetSocketsByUnit(this).Select(wrapper => 
                new FileDescriptor(wrapper.Socket.Handle.ToInt32(), "", Process.GetCurrentProcess().Id)).ToList();

            transaction.Add(new AlterTransactionContextTask("socket.fds", file_descriptors));
            transaction.Add(target_unit.GetActivationTransaction());

            if (!Descriptor.Accept)
            {
                transaction.Add(new IgnoreSocketsTask(this, target_unit));
            }

            transaction.Execute();
        }

        public override UnitDescriptor GetUnitDescriptor() => Descriptor;
        public override void SetUnitDescriptor(UnitDescriptor desc) => Descriptor = (SocketUnitDescriptor)desc;

        internal override Transaction GetActivationTransaction()
        {
            var transaction = new Transaction($"Activation transaction for unit {UnitName}");

            transaction.Add(new SetUnitStateTask(this, UnitState.Activating, UnitState.Inactive | UnitState.Failed));
            transaction.Add(new CreateRegisteredSocketTask(this));
            transaction.Add(new SetUnitStateTask(this, UnitState.Active, UnitState.Activating));
            transaction.Add(new UpdateUnitActivationTimeTask(this));

            return transaction;
        }

        internal override Transaction GetDeactivationTransaction()
        {
            var transaction = new Transaction($"Deactivation transaction for unit {UnitName}");

            transaction.Add(new SetUnitStateTask(this, UnitState.Deactivating, UnitState.Active));
            transaction.Add(new StopUnitSocketsTask(this));
            transaction.Add(new SetUnitStateTask(this, UnitState.Inactive, UnitState.Deactivating));

            return transaction;
        }

        public override Transaction GetReloadTransaction()
        {
            return new Transaction();
        }
    }
}
