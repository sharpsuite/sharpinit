using NLog;
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
    public class MountUnit : Unit
    {
        Logger Log = LogManager.GetCurrentClassLogger();

        public new MountUnitDescriptor Descriptor { get; set; }

        public MountUnit(string name, MountUnitDescriptor descriptor)
            : base(name, descriptor)
        {
        }

        public override UnitDescriptor GetUnitDescriptor() => Descriptor;
        public override void SetUnitDescriptor(UnitDescriptor desc) => Descriptor = (MountUnitDescriptor)desc;

        public bool ExternallyActivated { get; set; }

        public Transaction GetExternalActivationTransaction(string reason = "Unit externally activated")
        {
            ExternallyActivated = true;

            var transaction = new UnitStateChangeTransaction(this, $"Activation transaction for unit {UnitName}");

            transaction.Add(new SetUnitStateTask(this, UnitState.Active, UnitState.Any, reason));
            transaction.Add(new UpdateUnitActivationTimeTask(this));

            return transaction;
        }

        public Transaction GetExternalDeactivationTransaction(string reason = "Unit externally deactivated")
        {
            var transaction = new UnitStateChangeTransaction(this, $"Deactivation transaction for unit {UnitName}");

            transaction.Add(new SetUnitStateTask(this, UnitState.Inactive, UnitState.Any, reason));
            transaction.Add(new UpdateUnitActivationTimeTask(this));

            return transaction;
        }

        internal override Transaction GetActivationTransaction()
        {
            var transaction = new UnitStateChangeTransaction(this, $"Activation transaction for unit {UnitName}");

            transaction.Add(new RecordUnitStartupAttemptTask(this));
            transaction.Add(new SetUnitStateTask(this, UnitState.Activating, UnitState.Inactive | UnitState.Failed));
            transaction.Add(new MountTask(this));
            transaction.Add(new SetUnitStateTask(this, UnitState.Active, UnitState.Activating)); // This should be set by /proc/self/mountinfo
            transaction.Add(new UpdateUnitActivationTimeTask(this));

            transaction.OnFailure = transaction.OnTimeout = new SetUnitStateTask(this, UnitState.Failed);

            return transaction;
        }

        internal override Transaction GetDeactivationTransaction()
        {
            var transaction = new UnitStateChangeTransaction(this, $"Deactivation transaction for unit {UnitName}");

            transaction.Add(new CheckMountExternallyManagedTask(this));
            transaction.Add(new SetUnitStateTask(this, UnitState.Deactivating, UnitState.Active));
            transaction.Add(new UnmountTask(this));
            transaction.Add(new SetUnitStateTask(this, UnitState.Inactive, UnitState.Deactivating));

            return transaction;
        }

        public override Transaction GetReloadTransaction()
        {
            return new Transaction();
        }
    }
}
