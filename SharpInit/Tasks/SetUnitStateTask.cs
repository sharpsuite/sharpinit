using SharpInit.Units;
using System;
using System.Collections.Generic;
using System.Text;

namespace SharpInit.Tasks
{
    /// <summary>
    /// Conditionally sets the state of a unit.
    /// </summary>
    public class SetUnitStateTask : Task
    {
        public override string Type => "set-unit-state";

        public Unit Unit { get; set; }
        public UnitState AllowedInputStates { get; set; }
        public UnitState NextState { get; set; }

        /// <summary>
        /// Conditionally sets the state of a unit.
        /// </summary>
        /// <param name="unit">The unit to set the state of.</param>
        /// <param name="next_state">The next state of the unit.</param>
        /// <param name="allowed_input">The allowed set of starting states. If <paramref name="unit"/>'s state isn't in this parameter, 
        /// this task will return a ResultType of Failure.</param>
        public SetUnitStateTask(Unit unit, UnitState next_state, UnitState allowed_input = UnitState.Any)
        {
            Unit = unit;
            AllowedInputStates = allowed_input;
            NextState = next_state;
        }

        public override TaskResult Execute(TaskContext context)
        {
            if (AllowedInputStates != UnitState.Any && !AllowedInputStates.HasFlag(Unit.CurrentState))
                return new TaskResult(this, ResultType.Failure, "Invalid input Unit state.");

            Unit.SetState(NextState);
            return new TaskResult(this, ResultType.Success);
        }
    }
}
