using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;

using Mono.Unix.Native;

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

        public int KillSignal { get; set; }
        public int AfterKillSignal { get; set; }
        public int FinalKillSignal { get; set; }

        /// <summary>
        /// Stops a process.
        /// </summary>
        /// <param name="proc">The process to stop.</param>
        /// <param name="grace_period">On supported platforms, this is the gap (in milliseconds) between sending a SIGTERM and a SIGKILL.</param>
        public StopProcessTask(ProcessInfo proc, int kill = 15, int final_kill = 9, int after_kill = -1, int grace_period = 5000)
        {
            ProcessInfo = proc;
            GracePeriod = grace_period;
            KillSignal = kill;
            AfterKillSignal = after_kill;
            FinalKillSignal = final_kill;
        }

        public override TaskResult Execute(TaskContext context)
        {
            if (ProcessInfo.PlatformSupportsSignaling)
            {
                ProcessInfo.SendSignal((Signum)KillSignal);

                if (AfterKillSignal > 0)
                    ProcessInfo.SendSignal((Signum)AfterKillSignal);

                ProcessInfo.Process.WaitForExit(GracePeriod);

                if((ProcessInfo?.Process.HasExited) ?? false)
                    return new TaskResult(this, ResultType.Success);

                ProcessInfo.SendSignal((Signum)FinalKillSignal);
            }
            else
            {
                ProcessInfo.Process.Kill(true);
            }

            return new TaskResult(this, ResultType.Success);
        }
    }
}
