using SharpInit.Units;
using System;
using System.Collections.Generic;
using System.Text;

namespace SharpInit.Tasks
{
    public class CheckUnitActiveTask : Task
    {
        public override string Type => "check-unit-active";
        public string TargetUnit { get; set; }

        public CheckUnitActiveTask(string unit)
        {
            TargetUnit = unit;
        }

        public override TaskResult Execute()
        {
            var unit = UnitRegistry.GetUnit(TargetUnit);

            if (unit == null)
                return new TaskResult(this, ResultType.Failure, $"Unit {TargetUnit} is not loaded.");

            var state = unit.CurrentState;

            if (state == UnitState.Active)
                return new TaskResult(this, ResultType.Success);

            return new TaskResult(this, ResultType.Failure, $"Expected {TargetUnit} to be Active, it was instead {state}");
        }
    }
}
