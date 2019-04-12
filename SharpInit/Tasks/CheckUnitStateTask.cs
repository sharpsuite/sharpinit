using SharpInit.Units;
using System;
using System.Collections.Generic;
using System.Text;

namespace SharpInit.Tasks
{
    public class CheckUnitStateTask : Task
    {
        public override string Type => "check-unit-state";
        public UnitState ComparisonState { get; set; }
        public string TargetUnit { get; set; }
        public bool StopExecution { get; set; }

        public CheckUnitStateTask(UnitState state, string unit, bool stop = false)
        {
            ComparisonState = state;
            TargetUnit = unit;
            StopExecution = stop;
        }

        public override TaskResult Execute()
        {
            var failure_type = StopExecution ? ResultType.StopExecution : ResultType.Failure;
            var unit = UnitRegistry.GetUnit(TargetUnit);

            if (unit == null)
                return new TaskResult(this, failure_type, $"Unit {TargetUnit} is not loaded.");

            var state = unit.CurrentState;

            if (ComparisonState.HasFlag(state) || ComparisonState == UnitState.Any)
                return new TaskResult(this, ResultType.Success);

            return new TaskResult(this, failure_type, $"Expected {TargetUnit} to be Active, it was instead {state}");
        }
    }
}
