using SharpInit.Units;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SharpInit.Tasks
{
    public class UnitStateMatch
    {
        public UnitState StateMask { get; set; }
        public ResultType TaskResult { get; set; }

        public UnitStateMatch(UnitState mask, ResultType result)
        {
            StateMask = mask;
            TaskResult = result;
        }

        public static implicit operator UnitStateMatch(ValueTuple<UnitState, ResultType> t) => new UnitStateMatch(t.Item1, t.Item2);
        public static implicit operator UnitStateMatch(ValueTuple<ResultType, UnitState> t) => new UnitStateMatch(t.Item2, t.Item1);
        public static string PrintUnitState(UnitState state)
        {
            if (state == UnitState.Any)
                return "[any]";
            
            var flagged_states = new List<string>();

            foreach (var value in Enum.GetValues<UnitState>())
            {
                if (state.HasFlag(value))
                    flagged_states.Add(Enum.GetName<UnitState>(value));
            }

            if (flagged_states.Count == 0)
                return "[unknown]";

            if (flagged_states.Count == 1)
                return flagged_states[0];           
            
            return $"[{string.Join(", ", flagged_states)}]";
        }

        public override string ToString()
        {
            return $"({PrintUnitState(StateMask)} => {TaskResult})";
        }

    }
    
    /// <summary>
    /// Compares the state of a particular unit to the provided unit state.
    /// </summary>
    public class UnitStateConditionTask : AsyncTask
    {
        public override string Type => "unit-state-condition";
        public Unit TargetUnit { get; set; }
        public string TargetUnitName { get; set; }
        public List<UnitStateMatch> Matchers { get; set; }
        public ResultType Failure { get; set; }
        public TimeSpan Timeout { get; set; }

        /// <summary>
        /// Compares the state of <paramref name="unit"/> to the state matches in <paramref name="matchers"/>
        /// If the state of <paramref name="unit"/> matches any matcher in <paramref name="matchers"/>, the result type of the matcher is returned.
        /// Otherwise, <paramref name="unmatched"/> is returned.
        /// </summary>
        /// <param name="unit">The unit whose state is to be checked.</param>
        /// <param name="matchers">The unit state matchers to to check <paramref name="unit"/>'s state against.</param>
        /// <param name="unmatched">The task result to return if <paramref name="unit"/>'s state is not matched by any of <paramref name="matchers"/>.</param>
        public UnitStateConditionTask(Unit unit, ResultType unmatched = ResultType.Failure, params UnitStateMatch[] matchers)
        {
            TargetUnit = unit;
            TargetUnitName = unit?.UnitName;
            Matchers = matchers.ToList();
            Failure = unmatched;
            Timeout = TimeSpan.Zero;
        }

        /// <summary>
        /// Compares the state of <paramref name="unit"/> to the state matches in <paramref name="matchers"/>
        /// If the state of <paramref name="unit"/> matches any matcher in <paramref name="matchers"/>, the result type of the matcher is returned.
        /// Otherwise, if <paramref name="timeout"/> is set and non-zero, the tasks for that amount for <paramref name="unit"/>'s state to change to one that matches any
        /// of <paramref name="matchers"/>. If no matchers are matched in that timeframe, <paramref name="unmatched"/> | ResultType.Timeout is returned.
        /// If no timeout is set, <paramref name="unmatched"/> is returned as-is.
        /// </summary>
        /// <param name="unit">The unit whose state is to be checked.</param>
        /// <param name="matchers">The unit state matchers to to check <paramref name="unit"/>'s state against.</param>
        /// <param name="unmatched">The task result to return if <paramref name="unit"/>'s state is not matched by any of <paramref name="matchers"/>.</param>
        /// <param name="timeout">If <paramref name="unit"/>'s state does not immediately match any of <paramref name="matchers">, the amount of time to wait for it 
        /// to potentially transition to a matched-against unit state.</param>
        public UnitStateConditionTask(Unit unit, TimeSpan timeout, ResultType unmatched = ResultType.Failure, params UnitStateMatch[] matchers)
        {
            TargetUnit = unit;
            TargetUnitName = unit?.UnitName;
            Matchers = matchers.ToList();
            Failure = unmatched;
            Timeout = timeout;
        }

        private ResultType? GetMatch(UnitState state)
        {
            for (int i = 0; i < Matchers.Count; i++)
            {
                var matcher = Matchers[i];
                if (matcher.StateMask.HasFlag(state))
                    return matcher.TaskResult;
            }

            return null;
        }

        public async override System.Threading.Tasks.Task<TaskResult> ExecuteAsync(TaskContext context)
        {
            if (TargetUnit == null) { TargetUnit = Registry.GetUnit(TargetUnitName); }
            var unit = TargetUnit;

            if (unit == null)
                return new TaskResult(this, Failure, $"Unit {TargetUnitName} is not loaded.");

            var state = unit.CurrentState;
            var time_to_spare = Timeout;
            var start_time = Program.ElapsedSinceStartup();

            ResultType? result_type = null;
            
            while (result_type == null && time_to_spare > TimeSpan.Zero) 
            { 
                result_type = GetMatch(state = unit.CurrentState);

                if (result_type != null)
                    break;

                var wait_result = await unit.WaitForStateChange(time_to_spare);
                time_to_spare = Timeout - (Program.ElapsedSinceStartup() - start_time);

                if (!wait_result)
                    break;
            }
            
            if (result_type != null || (result_type = GetMatch(state = unit.CurrentState)) != null)
                return new TaskResult(this, result_type.Value);
            
            var failure_type = Failure;
            if (Timeout != default) // TODO: Figure out whether any consumers of this task might need the opposite of this behavior
                failure_type |= ResultType.Timeout;

            return new TaskResult(this, failure_type, $"{TargetUnit} was none of: " +
                $"[{string.Join(", ", Matchers)}], it was instead {state}");
        }
    }

    public static class UnitStateConditionExtensions
    {
        public static UnitStateConditionTask FailUnless(this Unit unit, UnitState target_mask) =>
            new UnitStateConditionTask(unit, unmatched: ResultType.Failure, (target_mask, ResultType.Success));
        public static UnitStateConditionTask FailUnless(this Unit unit, UnitState target_mask, TimeSpan timeout) =>
            new UnitStateConditionTask(unit, timeout: timeout, unmatched: ResultType.Failure, (target_mask, ResultType.Success));

        public static UnitStateConditionTask FailIf(this Unit unit, UnitState target_mask) =>
            new UnitStateConditionTask(unit, unmatched: ResultType.Success, (target_mask, ResultType.Failure));
        public static UnitStateConditionTask FailIf(this Unit unit, UnitState target_mask, TimeSpan timeout) =>
            new UnitStateConditionTask(unit, timeout: timeout, unmatched: ResultType.Success, (target_mask, ResultType.Failure));

        public static UnitStateConditionTask StopUnless(this Unit unit, UnitState target_mask) =>
            new UnitStateConditionTask(unit, unmatched: ResultType.StopExecution, (target_mask, ResultType.Success));
        public static UnitStateConditionTask StopUnless(this Unit unit, UnitState target_mask, TimeSpan timeout) =>
            new UnitStateConditionTask(unit, timeout: timeout, unmatched: ResultType.StopExecution, (target_mask, ResultType.Success));

        public static UnitStateConditionTask StopIf(this Unit unit, UnitState target_mask) =>
            new UnitStateConditionTask(unit, unmatched: ResultType.Success, (target_mask, ResultType.StopExecution));
        public static UnitStateConditionTask StopIf(this Unit unit, UnitState target_mask, TimeSpan timeout) =>
            new UnitStateConditionTask(unit, timeout: timeout, unmatched: ResultType.Success, (target_mask, ResultType.StopExecution));
        
        public static UnitStateConditionTask SkipIf(this Unit unit, UnitState target_mask) =>
            new UnitStateConditionTask(unit, unmatched: ResultType.Success, (target_mask, ResultType.StopExecution | ResultType.Ignorable));
        public static UnitStateConditionTask SkipIf(this Unit unit, UnitState target_mask, TimeSpan timeout) =>
            new UnitStateConditionTask(unit, timeout: timeout, unmatched: ResultType.Success, (target_mask, ResultType.StopExecution | ResultType.Ignorable));
    }
}
