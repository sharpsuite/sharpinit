using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using SharpInit.Units;

namespace SharpInit.Tasks
{
    public class AllocateSliceTask : Task
    {
        public override string Type => "allocate-slice";

        public Unit Unit { get; set; }

        public AllocateSliceTask(Unit unit)
        {
            Unit = unit;
        }

        public override TaskResult Execute(TaskContext context)
        {
            if (ServiceManager.CGroupManager == null || !ServiceManager.CGroupManager.CanCreateCGroups())
                return new TaskResult(this, ResultType.SoftFailure, "cgroups are not supported, or SharpInit is not privileged enough.");

            if (Unit.CGroup?.Exists == true)
                return new TaskResult(this, ResultType.Success);

            var parent_slice = Registry.GetUnit<SliceUnit>(Unit.ParentSlice);

            if (Unit is SliceUnit slice)
            {
                slice.CGroup = ServiceManager.CGroupManager.GetCGroup(slice.CGroupPath, create_if_missing: true);
            }
            else
            {
                var cgroup_path = parent_slice.CGroupPath + "/" + Unit.UnitName;
                Unit.CGroup = ServiceManager.CGroupManager.GetCGroup(cgroup_path, create_if_missing: true);
            }

            if (parent_slice != Unit)
            {
                parent_slice.RegisterChild(Unit);
            }

            Unit.CGroup.Update();

            return new TaskResult(this, ResultType.Success);
        }
    }
}
