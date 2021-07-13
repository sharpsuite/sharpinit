using SharpInit.Units;
using System;
using System.Collections.Generic;
using System.Text;
using NLog;

namespace SharpInit.Tasks
{
    /// <summary>
    /// Compares the state of a particular unit to the provided unit state.
    /// </summary>
    public class WaitForUnitStateTask : AsyncTask
    {
        private Logger Log = LogManager.GetCurrentClassLogger();
        public override string Type => "wait-for-unit-state";
        public UnitState ComparisonState { get; set; }
        public Unit TargetUnit { get; set; }
        public string TargetUnitName { get; set; }
        public bool StopExecution { get; set; }
        public bool Reverse { get; set; }
        public TimeSpan Timeout { get; set; }

        /// <summary>
        /// Compares the state of the unit <paramref name="unit"/> to <paramref name="state"/>.
        /// If <paramref name="unit"/>'s state matches <paramref name="state"/>, the task succeeds.
        /// Otherwise, if <paramref name="fail"/> is true, the task fails, and if it is set to false, the task returns a StopExecution.
        /// If <paramref name="reverse"/> is set to true, the task succeeds if <paramref name="unit"/>'s state does NOT match <paramref name="state"/>, and vice versa.
        /// </summary>
        /// <param name="state">The desired state of unit <paramref name="unit"/></param>
        /// <param name="unit">The unit to perform the state check on.</param>
        /// <param name="stop">true if the failure of this task should stop the parent transaction silently, false if the transaction should fail.</param>
        /// <param name="reverse">true if <paramref name="state"/> is the undesired unit state rather than the desired unit state. </param>
        public WaitForUnitStateTask(UnitState state, string unit, TimeSpan timeout, bool stop = false, bool reverse = false)
        {
            ComparisonState = state;
            TargetUnitName = unit;
            StopExecution = stop;
            Reverse = reverse;
            Timeout = timeout;
        }

        public WaitForUnitStateTask(UnitState state, Unit unit, TimeSpan timeout, bool stop = false, bool reverse = false) :
            this(state, unit.UnitName, timeout, stop, reverse) {}

        public WaitForUnitStateTask(UnitState state, string unit, int timeout, bool stop = false, bool reverse = false) :
            this(state, unit, timeout < 0 ? TimeSpan.FromMilliseconds(-1) : TimeSpan.FromMilliseconds(timeout), stop, reverse) {}

        public WaitForUnitStateTask(UnitState state, Unit unit, int timeout, bool stop = false, bool reverse = false) :
            this(state, unit, timeout < 0 ? TimeSpan.FromMilliseconds(-1) : TimeSpan.FromMilliseconds(timeout), stop, reverse) {}

        private bool InRightState(Unit unit)
        {
            var test = ComparisonState.HasFlag(unit.CurrentState);

            if (Reverse)
                test = !test;
            
            return test;
        }

        public async override System.Threading.Tasks.Task<TaskResult> ExecuteAsync(TaskContext context)
        {
            var failure_type = StopExecution ? ResultType.StopExecution : ResultType.Failure;
            failure_type |= ResultType.Timeout;

            if (TargetUnit == null) { TargetUnit = Registry.GetUnit(TargetUnitName); }
            var unit = TargetUnit;

            if (unit == null)
                return new TaskResult(this, failure_type, $"Unit {TargetUnitName} is not loaded.");

            var state = unit.CurrentState;
            bool test = false;
            var time_to_spare = Timeout;
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            while (!test && time_to_spare > TimeSpan.Zero) 
            { 
                test = InRightState(unit);

                if (test)
                    break;

                var wait_result = await unit.WaitForStateChange(time_to_spare);
                time_to_spare = Timeout - stopwatch.Elapsed;

                if (!wait_result)
                    break;
            }
            
            if (test || InRightState(unit))
                return new TaskResult(this, ResultType.Success);

            return new TaskResult(this, failure_type, $"Expected {TargetUnit} to {(Reverse ? "NOT be" : "be")} [{SetUnitStateTask.PrintUnitState(ComparisonState)}], it was instead {state}");
        }
    }
}
