using SharpInit.Units;
using System;
using System.Collections.Generic;
using System.Text;

namespace SharpInit.Tasks
{
    /// <summary>
    /// Updates the activation time of a unit.
    /// </summary>
    public class UpdateUnitActivationTimeTask : Task
    {
        public override string Type => "update-unit-activation-time";
        public Unit Unit { get; set; }

        /// <summary>
        /// Update the activation time of a unit.
        /// </summary>
        /// <param name="unit">The unit to update the activation time of.</param>
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
