using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace SharpInit.Tasks
{
    /// <summary>
    /// Barrier task used to synchronize execution of TransactionSynchronizationType.Explicit transactions.
    /// </summary>
    public class SynchronizationTask : Task
    {
        public override string Type => "synchronization";
        public TimeSpan Delay { get; set; }

        /// <summary>
        /// Acts as a synchronization barrier for an asynchronously executing Transaction .
        /// </summary>
        public SynchronizationTask() {}

        public override TaskResult Execute(TaskContext context)
        {
            return new TaskResult(this, ResultType.Success);
        }
    }
}
