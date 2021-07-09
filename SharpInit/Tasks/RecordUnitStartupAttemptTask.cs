using SharpInit.Units;
using System;
using System.Collections.Generic;
using System.Text;

namespace SharpInit.Tasks
{
    /// <summary>
    /// Updates the activation time of a unit.
    /// </summary>
    public class RecordUnitStartupAttemptTask : Task
    {
        public override string Type => "update-unit-activation-time";
        public Unit Unit { get; set; }

        /// <summary>
        /// Records an activation attempt for a Unit for startup throttling purposes.
        /// </summary>
        /// <param name="unit">The unit to record a startup attempt for.</param>
        public RecordUnitStartupAttemptTask(Unit unit)
        {
            Unit = unit;
        }

        public override TaskResult Execute(TaskContext context)
        {
            try 
            {
                if (Unit.StartupThrottle.IsThrottled())
                {
                    return new TaskResult(this, ResultType.Failure, "Unit is startup throttled.");
                }
                else
                {
                    // Clear the restart suppressed flag once we are no longer throttled.
                    // A restart that is not triggered manually will not get to this point: CanRestartNow() will filter it out much earlier.
                    // Therefore, we can assume that the current activation request is either manual or from a socket/timer.
                    Unit.RestartSuppressed = false;
                }

                Unit.StartupThrottle.RecordAction();
                return new TaskResult(this, ResultType.Success);
            }
            catch
            {
                return new TaskResult(this, ResultType.SoftFailure, "Failed to record startup attempt");
            }
        }
    }
}
