using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NLog;
using SharpInit.Tasks;

namespace SharpInit.Units
{
    public class TargetUnit : Unit
    {
        Logger Log = LogManager.GetCurrentClassLogger();
        
        public new UnitDescriptor Descriptor { get; set; }

        public TargetUnit(string name, UnitDescriptor descriptor) 
            : base(name, descriptor)
        {

        }

        public override UnitDescriptor GetUnitDescriptor() => Descriptor;
        public override void SetUnitDescriptor(UnitDescriptor desc) => Descriptor = desc;

        public override IEnumerable<Dependency> GetDefaultDependencies()
        {
            foreach (var base_dep in base.GetDefaultDependencies())
                yield return base_dep;

            foreach (var wanted in Descriptor.Wants.Concat(Descriptor.Requires))
                if (UnitRegistry.GetUnit(wanted)?.Descriptor?.DefaultDependencies ?? false != false)
                    yield return new OrderingDependency(left: UnitName, right: wanted, from: UnitName, type: OrderingDependencyType.After);
        }

        internal override Transaction GetActivationTransaction()
        {
            var transaction = new UnitStateChangeTransaction(this, $"Activation transaction for unit {UnitName}");

            transaction.Add(new SetUnitStateTask(this, UnitState.Activating));
            transaction.Add(new SetUnitStateTask(this, UnitState.Active, UnitState.Activating));
            transaction.Add(new UpdateUnitActivationTimeTask(this));

            return transaction;
        }

        internal override Transaction GetDeactivationTransaction()
        {
            var transaction = new UnitStateChangeTransaction(this, $"Deactivation transaction for unit {UnitName}");

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
