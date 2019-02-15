using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace SharpInit.Tasks
{
    public class DelayTask : Task
    {
        public override string Type => "delay";
        public TimeSpan Delay { get; set; }

        public DelayTask(TimeSpan delay)
        {
            Delay = delay;
        }

        public DelayTask(int milliseconds) :
            this(TimeSpan.FromMilliseconds(milliseconds))
        { }

        public override TaskResult Execute()
        {
            Thread.Sleep(Delay);
            return new TaskResult(this, ResultType.Success);
        }
    }
}
