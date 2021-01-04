using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace SharpInit.Tasks
{
    /// <summary>
    /// Stops a process.
    /// </summary>
    public class StopProcessTask : Task
    {
        public override string Type => "stop-process";
        public ProcessInfo ProcessInfo { get; set; }
        public int GracePeriod { get; set; }

        /// <summary>
        /// Stops a process.
        /// </summary>
        /// <param name="proc">The process to stop.</param>
        /// <param name="grace_period">On supported platforms, this is the gap (in milliseconds) between sending a SIGTERM and a SIGKILL.</param>
        public StopProcessTask(ProcessInfo proc, int grace_period = 5000)
        {
            ProcessInfo = proc;
            GracePeriod = grace_period;
        }

        public override TaskResult Execute()
        {
            if (ProcessInfo.PlatformSupportsSignaling)
            {
                ProcessInfo.SendSignal(Mono.Unix.Native.Signum.SIGTERM);
                ProcessInfo.Process.WaitForExit(GracePeriod);

                if((ProcessInfo?.Process.HasExited) ?? false)
                    return new TaskResult(this, ResultType.Success);

                ProcessInfo.SendSignal(Mono.Unix.Native.Signum.SIGKILL);
            }
            else
            {
                ProcessInfo.Process.Kill(true);
            }

            return new TaskResult(this, ResultType.Success);
        }
    }
}
