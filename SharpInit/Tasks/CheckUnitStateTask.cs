using SharpInit.Units;
using System;
using System.Collections.Generic;
using System.Text;

namespace SharpInit.Tasks
{
    /// <summary>
    /// Compares the state of a particular unit to the provided unit state.
    /// </summary>
    public class CheckUnitStateTask : Task
    {
        public override string Type => "check-unit-state";
        public UnitState ComparisonState { get; set; }
        public string TargetUnit { get; set; }
        public bool StopExecution { get; set; }

        /// <summary>
        /// Compares the state of the unit <paramref name="unit"/> to <paramref name="state"/>.
        /// </summary>
        /// <param name="state">The desired state of unit <paramref name="unit"/></param>
        /// <param name="unit">The unit to perform the state check on.</param>
        /// <param name="stop">true if the failure of this task should stop the parent transaction silently, false if the transaction should fail.</param>
        public CheckUnitStateTask(UnitState state, string unit, bool stop = false)
        {
            ComparisonState = state;
            TargetUnit = unit;
            StopExecution = stop;
        }

        public override TaskResult Execute(TaskContext context)
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
