using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using NLog;

using SharpInit.Platform.Unix;
using SharpInit.Tasks;

namespace SharpInit.Units
{
    public class SliceUnit : Unit
    {
        Logger Log = LogManager.GetCurrentClassLogger();
        
        public new UnitDescriptor Descriptor { get; set; }

        public string CGroupPath => (ServiceManager.CGroupManager?.RootCGroup?.Path ?? "") + GetCGroupPath(UnitName);

        public List<string> Children { get; set; }

        public static string GetCGroupPath(string slice_name)
        {
            if (!slice_name.EndsWith(".slice"))
                return null;

            if (slice_name == "-.slice")
                return "";

            var current = slice_name;
            var parent = GetParentSlice(current);
            var path = current;

            while (parent != "-.slice")
            {
                path = parent + "/" + path;
                current = parent;
                parent = GetParentSlice(parent);
            }

            return "/" + path;
        }

        public SliceUnit(string name, UnitDescriptor descriptor) 
            : base(name, descriptor)
        {
            if (UnitParser.GetUnitParameter(name) != "")
                throw new Exception($".slice units cannot have parameters");
            
            ParentSlice = GetParentSlice(name);
            Children = new List<string>();
        }

        public void RegisterChild(Unit unit)
        {
            if (unit.ParentSlice != UnitName)
            {
                throw new Exception($"Asked to register {unit.UnitName} with parent slice {unit.ParentSlice} to slice {UnitName}");
            }

            if (!Children.Contains(unit.UnitName))
                Children.Add(unit.UnitName);
            
            CGroup?.Update();
        }

        public override UnitDescriptor GetUnitDescriptor() => Descriptor;
        public override void SetUnitDescriptor(UnitDescriptor desc) => Descriptor = desc;

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

        public static string GetParentSlice(string slice_name)
        {
            if (!slice_name.EndsWith(".slice"))
                return null;
            
            slice_name = slice_name.Substring(0, slice_name.Length - ".slice".Length);
            var unescaped = StringEscaper.UnescapePath(slice_name);
            var parts = unescaped.Split('/', StringSplitOptions.RemoveEmptyEntries);

            if (!parts.Any())
                return "-.slice";
            
            return StringEscaper.EscapePath("/" + string.Join("/", parts.Take(parts.Length - 1))) + ".slice";
        }
    }
}
