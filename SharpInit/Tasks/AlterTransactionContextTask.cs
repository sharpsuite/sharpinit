using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpInit.Tasks
{
    public class AlterTransactionContextTask : Task
    {
        public override string Type => "alter-transaction-context";

        private string ContextKey { get; set; }
        private object ContextValue { get; set; }

        public AlterTransactionContextTask(string key, object value)
        {
            ContextKey = key;
            ContextValue = value;
        }

        public override TaskResult Execute(TaskContext context)
        {
            context[ContextKey] = ContextValue;
            return new TaskResult(this, ResultType.Success);
        }
    }
}
