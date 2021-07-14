using SharpInit.Units;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SharpInit.Tasks
{
    /// <summary>
    /// Activates or deactivates a unit, calculating the transaction at runtime.
    /// </summary>
    public class LateBoundUnitActivationTask : AsyncTask
    {
        public override string Type => $"late-bound-unit-{StateChangeType.ToString().ToLowerInvariant()}";
        public string UnitName { get; set; }
        public UnitStateChangeType StateChangeType { get; set; }
        public string Reason { get; set; }
        public UnitStateChangeTransaction GeneratedTransaction { get; set; }

        /// <summary>
        /// Activates or deactivates a unit, calculating the transaction at runtime.
        /// </summary>
        /// <param name="unit">The unit to activate or deactivate.</param>
        /// <param name="change_type">Whether to activate or deactivate the unit.</param>
        public LateBoundUnitActivationTask(string unit, UnitStateChangeType change_type, string reason = null)
        {
            UnitName = unit;
            StateChangeType = change_type;
            Reason = reason;
        }

        public static LateBoundUnitActivationTask CreateActivationTransaction(string unit, string reason = null) =>
            new LateBoundUnitActivationTask(unit, UnitStateChangeType.Activation, reason);
        public static LateBoundUnitActivationTask CreateActivationTransaction(Unit unit, string reason = null) =>
            CreateActivationTransaction(unit.UnitName, reason);

        public static LateBoundUnitActivationTask CreateDeactivationTransaction(string unit, string reason = null) =>
            new LateBoundUnitActivationTask(unit, UnitStateChangeType.Deactivation, reason);
        public static LateBoundUnitActivationTask CreateDeactivationTransaction(Unit unit, string reason = null) =>
            CreateDeactivationTransaction(unit.UnitName, reason);

        public async override System.Threading.Tasks.Task<TaskResult> ExecuteAsync(TaskContext context)
        {
            try 
            {
                UnitStateChangeTransaction tx;

                if (StateChangeType == UnitStateChangeType.Activation)
                    tx = ServiceManager.Planner.CreateActivationTransaction(UnitName, Reason);
                else if (StateChangeType == UnitStateChangeType.Deactivation)
                    tx = ServiceManager.Planner.CreateDeactivationTransaction(UnitName, Reason);
                else
                    return new TaskResult(this, ResultType.Failure, $"Unrecognized state change type {StateChangeType}");
                
                GeneratedTransaction = tx;
                
                return await Runner.ExecuteAsync(tx, context);
            }
            catch
            {
                return new TaskResult(this, ResultType.SoftFailure, $"Late bound unit {(StateChangeType == UnitStateChangeType.Deactivation ? "de" : "")}activation failed for {UnitName}");
            }
        }
    }
}
