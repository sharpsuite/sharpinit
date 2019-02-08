using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace SharpInit.Tasks
{
    public class StopProcessTask : Task
    {
        public override string Type => "stop-process";
        public ProcessInfo ProcessInfo { get; set; }
        public int GracePeriod { get; set; }

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
                Thread.Sleep(GracePeriod);

                try
                {
                    Process.GetProcessById(ProcessInfo.Id);
                }
                catch
                {
                    return new TaskResult(this, ResultType.Success);
                }

                ProcessInfo.SendSignal(Mono.Unix.Native.Signum.SIGKILL);
            }
            else
            {
                ProcessInfo.Process.Kill();
            }

            return new TaskResult(this, ResultType.Success);
        }
    }
}
