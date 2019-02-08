using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace SharpInit.Tasks
{
    public class RunUnregisteredProcessTask : Task
    {
        public override string Type => "run-unregistered-process";
        public ProcessStartInfo ProcessStartInfo { get; set; }
        public int ExecutionTime = -1;

        public RunUnregisteredProcessTask(ProcessStartInfo psi, int time = -1)
        {
            ProcessStartInfo = psi;
            ExecutionTime = time;
        }

        public override TaskResult Execute()
        {
            if (ProcessStartInfo == null)
                return new TaskResult(this, ResultType.Failure, "No ProcessStartInfo supplied.");

            try
            {
                var process = Process.Start(ProcessStartInfo);

                if (ExecutionTime == -1)
                    return new TaskResult(this, ResultType.Success);
                else
                {
                    if (!process.WaitForExit(ExecutionTime))
                    {
                        process.Kill();
                        return new TaskResult(this, ResultType.Timeout, "The process did not exit in the given amount of time.");
                    }
                    else
                        return new TaskResult(this, ResultType.Success);
                }
            }
            catch (Exception ex)
            {
                return new TaskResult(this, ResultType.Failure, ex.Message);
            }
        }
    }
}
