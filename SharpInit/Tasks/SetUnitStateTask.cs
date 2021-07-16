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
        public string Reason { get; set; }
        public bool FailSilently { get; set; }

        /// <summary>
        /// Conditionally sets the state of a unit.
        /// </summary>
        /// <param name="unit">The unit to set the state of.</param>
        /// <param name="next_state">The next state of the unit.</param>
        /// <param name="allowed_input">The allowed set of starting states. If <paramref name="unit"/>'s state isn't in this parameter, 
        /// this task will return a ResultType of Failure.</param>
        /// <param name="reason">The reason for the unit state change. If there is a "failure" TaskResult in the TaskContext,
        /// this parameter is ignored.</param>
        public SetUnitStateTask(Unit unit, UnitState next_state, UnitState allowed_input = UnitState.Any, string reason = null, bool fail_silently = false)
        {
            Unit = unit;
            AllowedInputStates = allowed_input;
            NextState = next_state;
            Reason = reason;
            FailSilently = fail_silently;
        }

        public override TaskResult Execute(TaskContext context)
        {
            if (AllowedInputStates != UnitState.Any && !AllowedInputStates.HasFlag(Unit.CurrentState))
            {
                var failure_type = FailSilently ? ResultType.SoftFailure : ResultType.Failure;
                return new TaskResult(this, failure_type, $"{Unit.UnitName} has invalid input state {PrintUnitState(Unit.CurrentState)}, was expecting {PrintUnitState(AllowedInputStates)}");
            }
            
            if (context.Has<TaskResult>("failure"))
            {
                var fail_result = context.Get<TaskResult>("failure");
                Unit.SetState(NextState, $"{fail_result.Task.Type} failed: {fail_result.Message}");
                context.Values.Remove("failure");
            }
            else if (context.Has<string>("state_change_reason"))
            {
                Unit.SetState(NextState, context.Get<string>("state_change_reason"));
            }
            else
            {
                Unit.SetState(NextState, Reason);
            }
            return new TaskResult(this, ResultType.Success);
        }

        public static string PrintUnitState(UnitState state)
        {
            if (state == UnitState.Any)
                return "any";
            
            var flagged_states = new List<string>();

            foreach (var value in Enum.GetValues<UnitState>())
            {
                if (state.HasFlag(value))
                    flagged_states.Add(Enum.GetName<UnitState>(value));
            }

            if (flagged_states.Count == 0)
                return "unknown";

            if (flagged_states.Count == 1)
                return flagged_states[0];           
            
            return $"[{string.Join(", ", flagged_states)}]";
        }
    }
}
