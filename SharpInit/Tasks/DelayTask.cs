using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace SharpInit.Tasks
{
    /// <summary>
    /// Delays execution for the provided period of time.
    /// </summary>
    public class DelayTask : AsyncTask
    {
        public override string Type => "delay";
        public TimeSpan Delay { get; set; }

        /// <summary>
        /// Delays execution for <paramref name="delay"/>.
        /// </summary>
        public DelayTask(TimeSpan delay)
        {
            Delay = delay;
        }

        /// <summary>
        /// Delays execution for <paramref name="milliseconds"/> milliseconds.
        /// </summary>
        /// <param name="milliseconds">The amount of time (in milliseconds) to delay execution for.</param>
        public DelayTask(int milliseconds) :
            this(TimeSpan.FromMilliseconds(milliseconds))
        { }

        public async override System.Threading.Tasks.Task<TaskResult> ExecuteAsync(TaskContext context)
        {
            await System.Threading.Tasks.Task.Delay(Delay);
            return new TaskResult(this, ResultType.Success);
        }
    }
}
