using SharpInit.Platform;
using SharpInit.Units;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SharpInit.Tasks
{
    /// <summary>
    /// Starts a process and associates it with the service manager of a particular unit.
    /// </summary>
    public class RunRegisteredProcessTask : Task
    {
        public override string Type => "run-registered-process";
        public ProcessStartInfo ProcessStartInfo { get; set; }
        public Unit Unit { get; set; }

        /// <summary>
        /// Starts a process with the parameters outlined in <paramref name="psi"/> and associates it with the service manager of <paramref name="unit"/>.
        /// </summary>
        /// <param name="psi">The ProcessStartInfo that defines the parameters of the process to be executed.</param>
        /// <param name="unit">The Unit to associate the newly created process with.</param>
        public RunRegisteredProcessTask(ProcessStartInfo psi, Unit unit)
        {
            ProcessStartInfo = psi;
            Unit = unit;
        }

        public override TaskResult Execute(TaskContext context)
        {
            if (ProcessStartInfo == null || Unit == null)
                return new TaskResult(this, ResultType.Failure, "No ProcessStartInfo or Unit supplied.");

            try
            {
                var fds = context.Get<List<FileDescriptor>>("socket.fds");

                if (fds?.Count > 0)
                {
                    if (ProcessStartInfo.Environment == null)
                    {
                        ProcessStartInfo.Environment = new Dictionary<string, string>();
                    }

                    ProcessStartInfo.Environment["LISTEN_FDS"] = fds.Count.ToString();
                    ProcessStartInfo.Environment["LISTEN_PID"] = "fill";
                    ProcessStartInfo.Environment["LISTEN_FDNAMES"] = string.Join(':', fds.Select(fd => fd.Name));
                    ProcessStartInfo.Environment["LISTEN_FDNUMS"] = string.Join(':', fds.Select(fd => fd.Number));
                }

                Unit.ServiceManager.StartProcess(Unit, ProcessStartInfo);
                return new TaskResult(this, ResultType.Success);
            }
            catch (Exception ex)
            {
                return new TaskResult(this, ResultType.Failure, ex.Message);
            }
        }
    }
}
