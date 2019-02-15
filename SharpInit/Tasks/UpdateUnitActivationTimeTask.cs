using SharpInit.Units;
using System;
using System.Collections.Generic;
using System.Text;

namespace SharpInit.Tasks
{
    public class UpdateUnitActivationTimeTask : Task
    {
        public override string Type => "update-unit-activation-time";
        public Unit Unit { get; set; }

        public UpdateUnitActivationTimeTask(Unit unit)
        {
            Unit = unit;
        }

        public override TaskResult Execute()
        {
            Unit.ActivationTime = DateTime.UtcNow;
            return new TaskResult(this, ResultType.Success);
        }
    }
}
