using SharpInit.Units;
using System;
using System.Collections.Generic;
using System.Text;

namespace SharpInit.Tasks
{
    public class SetUnitStateTask : Task
    {
        public override string Type => "set-unit-state";

        public Unit Unit { get; set; }
        public UnitState AllowedInputStates { get; set; }
        public UnitState NextState { get; set; }

        public SetUnitStateTask(Unit unit, UnitState next_state, UnitState allowed_input = UnitState.Any)
        {
            Unit = unit;
            AllowedInputStates = allowed_input;
            NextState = next_state;
        }

        public override TaskResult Execute()
        {
            if (AllowedInputStates != UnitState.Any && !AllowedInputStates.HasFlag(Unit.CurrentState))
                return new TaskResult(this, ResultType.Failure, "Invalid input Unit state.");

            Unit.SetState(NextState);
            return new TaskResult(this, ResultType.Success);
        }
    }
}
