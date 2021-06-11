using NLog;
using SharpInit.Tasks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

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

        internal void RaiseSocketActivated(Socket socket) => SocketActivated?.Invoke(this, socket);

        private void HandleSocketActivated(Unit unit, Socket socket)
        {
            var transaction = new Transaction($"Socket activation for {this.UnitName}");

            transaction.Add(new AlterTransactionContextTask("socket.fds", ))
        }

        public override UnitDescriptor GetUnitDescriptor() => Descriptor;
        public override void SetUnitDescriptor(UnitDescriptor desc) => Descriptor = (SocketUnitDescriptor)desc;

        internal override Transaction GetActivationTransaction()
        {
            var transaction = new Transaction($"Activation transaction for unit {UnitName}");

            transaction.Add(new SetUnitStateTask(this, UnitState.Activating, UnitState.Inactive | UnitState.Failed));
            transaction.Add(new SetUnitStateTask(this, UnitState.Active, UnitState.Activating));
            transaction.Add(new UpdateUnitActivationTimeTask(this));

            return transaction;
        }

        internal override Transaction GetDeactivationTransaction()
        {
            var transaction = new Transaction($"Deactivation transaction for unit {UnitName}");

            transaction.Add(new SetUnitStateTask(this, UnitState.Deactivating, UnitState.Active));
            transaction.Add(new SetUnitStateTask(this, UnitState.Inactive, UnitState.Deactivating));

            return transaction;
        }

        public override Transaction GetReloadTransaction()
        {
            return new Transaction();
        }
    }
}
