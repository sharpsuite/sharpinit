using SharpInit.Units;
using System;
using System.Collections.Generic;
using System.Text;

namespace SharpInit.Tasks
{
    /// <summary>
    /// Stops all processes registered under a unit.
    /// </summary>
    public class StopUnitProcessesTask : Task
    {
        public override string Type => "stop-processes-by-unit";
        public Unit Unit { get; set; }
        public int GracePeriod { get; set; }

        /// <summary>
        /// Stops all processes registered under a unit.
        /// </summary>
        /// <param name="unit">The unit that owns the processes.</param>
        /// <param name="grace_period">On supported platforms, this is the gap (in milliseconds) between sending a SIGTERM and a SIGKILL.</param>
        public StopUnitProcessesTask(Unit unit, int grace_period = 5000)
        {
            Unit = unit;
            GracePeriod = grace_period;
        }

        public override TaskResult Execute(TaskContext context)
        {
            var service_manager = Unit.ServiceManager;

            if (!service_manager.ProcessesByUnit.ContainsKey(Unit))
                return new TaskResult(this, ResultType.Success);

            bool failed = false;
            var processes = service_manager.ProcessesByUnit[Unit].ToArray();

            foreach (var process in processes)
            {
                var stop_process_task = new StopProcessTask(process, GracePeriod);
                var result = stop_process_task.Execute(context);

                if (result.Type != ResultType.Success)
                    failed = true; // let's keep killing all processes we can anyway 
            }

            return new TaskResult(this, failed ? ResultType.SoftFailure : ResultType.Success);
        }
    }
}
