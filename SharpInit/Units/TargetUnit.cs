using System;
using System.Collections.Generic;
using System.Text;
using NLog;
using SharpInit.Tasks;

namespace SharpInit.Units
{
    public class TargetUnit : Unit
    {
        Logger Log = LogManager.GetCurrentClassLogger();
        
        public new UnitDescriptor Descriptor { get; set; }

        public TargetUnit(UnitDescriptor descriptor) 
            : base(descriptor)
        {

        }

        public override UnitDescriptor GetUnitDescriptor() => Descriptor;
        public override void SetUnitDescriptor(UnitDescriptor desc) => Descriptor = desc;

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
