using SharpInit.Units;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SharpInit.Tasks
{
    /// <summary>
    /// Updates the main process ID of a unit.
    /// </summary>
    public class SetMainPidTask : Task
    {
        public override string Type => "set-main-pid";
        public Unit Unit { get; set; }
        public RunRegisteredProcessTask StartProcessTask { get; set; }

        /// <summary>
        /// Updates the main process ID of a unit.
        /// </summary>
        /// <param name="unit">The unit to record the main process ID of.</param>
        public SetMainPidTask(Unit unit, RunRegisteredProcessTask start_process_task = null)
        {
            Unit = unit;
            StartProcessTask = start_process_task;
        }

        public override TaskResult Execute(TaskContext context)
        {
            try 
            {
                if (Unit is ServiceUnit service)
                {
                    if (service.Descriptor.ServiceType == ServiceType.Forking)
                    {
                        if (!string.IsNullOrWhiteSpace(service.Descriptor.PIDFile))
                        {
                            if (File.Exists(service.Descriptor.PIDFile))
                            {
                                try
                                {
                                    var contents = File.ReadAllText(service.Descriptor.PIDFile).Trim();

                                    if (int.TryParse(contents, out int pid))
                                    {
                                        service.MainProcessId = pid;
                                    }
                                }
                                catch
                                {}
                            }
                        }
                        else if (service.Descriptor.GuessMainPID)
                        {
                            if (service.CGroup != null && service.CGroup.ManagedByUs)
                            {
                                service.CGroup.Update();
                                service.MainProcessId = !service.CGroup.ChildProcesses.Any() ? -1 : service.CGroup.ChildProcesses.Min();
                            }
                        }
                    }
                    else if (StartProcessTask != null)
                    {
                        service.MainProcessId = StartProcessTask.Process?.Id ?? -1;
                    }
                }
                else if (Unit.CGroup != null && Unit.CGroup.ManagedByUs)
                {
                    Unit.CGroup.Update();
                    Unit.MainProcessId = !Unit.CGroup.ChildProcesses.Any() ? -1 : Unit.CGroup.ChildProcesses.Min();
                }

                if (Unit.MainProcessId == -1)
                    return new TaskResult(this, ResultType.SoftFailure, $"Could not obtain main PID");

                return new TaskResult(this, ResultType.Success);
            }
            catch
            {
                return new TaskResult(this, ResultType.SoftFailure, "Failed to record startup attempt");
            }
        }
    }
}
