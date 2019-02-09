using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SharpInit.Tasks
{
    public class Transaction : Task
    {
        public override string Type => "transaction";
        public List<Task> Tasks = new List<Task>();
        public TransactionErrorHandlingMode ErrorHandlingMode { get; set; }

        public Transaction()
        {

        }

        public Transaction(params IEnumerable<Task>[] tasks)
        {
            Add(tasks);
        }

        public void Add(Task task)
        {
            Tasks.Add(task);
        }

        public void Add(params IEnumerable<Task>[] tasks)
        {
            Tasks.AddRange(tasks.SelectMany(t => t));
        }

        public override TaskResult Execute()
        {
            foreach (var task in Tasks)
            {
                var result = task.Execute();

                if (ErrorHandlingMode != TransactionErrorHandlingMode.Ignore &&
                    result.Type != ResultType.Success &&
                    !result.Type.HasFlag(ResultType.Ignorable))
                {
                    // fatal failure
                    return result;
                }
            }

            return new TaskResult(this, ResultType.Success);
        }
    }

    public enum TransactionErrorHandlingMode
    {
        Fail,
        Ignore
    }
}
