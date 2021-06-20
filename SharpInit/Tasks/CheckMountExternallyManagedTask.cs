using System;
using SharpInit.Units;

namespace SharpInit.Tasks
{
    public class CheckMountExternallyManagedTask : Task
    {
        public override string Type => "check-mount-externally-managed";

        public MountUnit Unit { get; set; }

        public CheckMountExternallyManagedTask(MountUnit unit)
        {
            Unit = unit;
        }

        public override TaskResult Execute(TaskContext context)
        {
            try
            {
                if (Unit == null)
                    throw new Exception("No mount unit given");
                
                if (Unit.ExternallyActivated)
                    return new TaskResult(this, ResultType.StopExecution, "Mount is externally managed");
                
                return new TaskResult(this, ResultType.Success);
            }
            catch (Exception ex)
            {
                return new TaskResult(this, ResultType.Failure | ResultType.StopExecution, ex.Message);
            }
        }
    }
}