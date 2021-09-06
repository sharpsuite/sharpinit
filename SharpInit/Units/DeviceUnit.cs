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
    public class DeviceUnit : Unit
    {
        static Dictionary<(string, UnitStateChangeType), (string, string)> CustomStatusMessages = new Dictionary<(string, UnitStateChangeType), (string, string)>()
        {
            {("pre", UnitStateChangeType.Activation), (null, "Expecting device {0}...")},

            {("success", UnitStateChangeType.Activation), ("  OK  ", "Found device {0}.")},
            {("success", UnitStateChangeType.Deactivation), ("  OK  ", "Removed device {0}.")},

            {("timeout", UnitStateChangeType.Activation), (" TIME ", "Timed out waiting for device {0}.")},
        };

        public override Dictionary<(string, UnitStateChangeType), (string, string)> StatusMessages => CustomStatusMessages;
        Logger Log = LogManager.GetCurrentClassLogger();

        public new UnitDescriptor Descriptor { get; set; }

        public bool IsActive { get; set; }

        public DeviceUnit(string name, UnitDescriptor descriptor)
            : base(name, descriptor)
        {
        }

        public override UnitDescriptor GetUnitDescriptor() => Descriptor;
        public override void SetUnitDescriptor(UnitDescriptor desc) => Descriptor = desc;

        public Transaction GetExternalActivationTransaction(string reason = "Device externally activated")
        {
            var transaction = new UnitStateChangeTransaction(this, UnitStateChangeType.Activation);
            transaction.Precheck = this.StopIf(UnitState.Active);

            transaction.Add(new SetUnitStateTask(this, UnitState.Active, UnitState.Any, reason));
            transaction.Add(new UpdateUnitActivationTimeTask(this));

            return transaction;
        }

        public Transaction GetExternalDeactivationTransaction(string reason = "Device externally deactivated")
        {
            var transaction = new UnitStateChangeTransaction(this, UnitStateChangeType.Deactivation);
            transaction.Precheck = this.StopIf(UnitState.Inactive);

            transaction.Add(new SetUnitStateTask(this, UnitState.Inactive, UnitState.Any, reason));
            transaction.Add(new UpdateUnitActivationTimeTask(this));

            return transaction;
        }

        internal override Transaction GetActivationTransaction()
        {
            var transaction = new UnitStateChangeTransaction(this, UnitStateChangeType.Activation);
            transaction.Add(this.FailUnless(UnitState.Active, TimeSpan.Zero));

            return transaction;
        }

        internal override Transaction GetDeactivationTransaction()
        {
            var transaction = new UnitStateChangeTransaction(this, UnitStateChangeType.Deactivation);
            transaction.Add(this.FailUnless(UnitState.Inactive, TimeSpan.Zero));

            return transaction;
        }

        public override Transaction GetReloadTransaction()
        {
            return new Transaction();
        }
    }
}
