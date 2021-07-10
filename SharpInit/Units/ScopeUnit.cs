using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using NLog;

using SharpInit.Platform.Unix;
using SharpInit.Tasks;

namespace SharpInit.Units
{
    public class ScopeUnit : Unit
    {
        Logger Log = LogManager.GetCurrentClassLogger();
        
        public new ExecUnitDescriptor Descriptor { get; set; }

        public List<string> Children { get; set; }

        public ScopeUnit(string name, ExecUnitDescriptor descriptor) 
            : base(name, descriptor)
        {
            if (UnitParser.GetUnitParameter(name) != "")
                throw new Exception($".scope units cannot have parameters");
        }

        public override ExecUnitDescriptor GetUnitDescriptor() => Descriptor;
        public override void SetUnitDescriptor(UnitDescriptor desc) 
        {
            Descriptor = (ExecUnitDescriptor)desc;

            if (string.IsNullOrWhiteSpace(ParentSlice))
                ParentSlice = Descriptor.Slice; // This is sticky (if set once, can't be unset)
        }

        public override IEnumerable<Dependency> GetDefaultDependencies()
        {
            foreach (var base_dep in base.GetDefaultDependencies())
                yield return base_dep;

            if (Descriptor.DefaultDependencies)
            {
                yield return new OrderingDependency(left: "shutdown.target", right: UnitName, from: UnitName, type: OrderingDependencyType.After);
                yield return new RequirementDependency(left: UnitName, right: "shutdown.target", from: UnitName, type: RequirementDependencyType.Conflicts);
            }
        }

        internal override Transaction GetActivationTransaction()
        {
            var transaction = new UnitStateChangeTransaction(this, $"Activation transaction for slice {UnitName}");

            transaction.Add(new SetUnitStateTask(this, UnitState.Activating));
            transaction.Add(new AllocateSliceTask(this));
            transaction.Add(new SetUnitStateTask(this, UnitState.Active, UnitState.Activating));
            transaction.Add(new UpdateUnitActivationTimeTask(this));

            return transaction;
        }

        internal override Transaction GetDeactivationTransaction()
        {
            var transaction = new UnitStateChangeTransaction(this, $"Deactivation transaction for slice {UnitName}");

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
