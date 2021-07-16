using System;
using System.Collections.Generic;
using System.Text;

using SharpInit.Units;

namespace SharpInit.Tasks
{
    /// <summary>
    /// A generalized unit of work, used to build transactions.
    /// </summary>
    public abstract class Task
    {
        public TaskExecution Execution { get; internal set; }
        public TaskRunner Runner => Execution?.Runner;
        public ServiceManager ServiceManager => Execution?.ServiceManager;
        public UnitRegistry Registry => Execution?.Registry;

        public long Identifier { get; set; }

        public abstract string Type { get; }
        public abstract TaskResult Execute(TaskContext context);

        protected TaskResult ExecuteBlocking(Task task, TaskContext context) => Execution.ExecuteBlocking(task, context);
        protected TaskResult ExecuteBlocking(TaskExecution exec) => Execution.ExecuteBlocking(exec);

        protected async System.Threading.Tasks.Task<TaskResult> ExecuteAsync(Task task, TaskContext context) => await Execution.ExecuteAsync(task, context);
        protected async System.Threading.Tasks.Task<TaskResult> ExecuteAsync(TaskExecution exec) => await Execution.ExecuteAsync(exec);

        public override string ToString()
        {
            if (Identifier > 0 && Identifier < 65536)
                return $"{Type}/{Identifier}";

            return $"{Type}/{Identifier,16:x16}";
        }
    }

    public abstract class AsyncTask : Task
    {
        public override TaskResult Execute(TaskContext context) => ExecuteAsync(context).Result;
        public abstract System.Threading.Tasks.Task<TaskResult> ExecuteAsync(TaskContext context);

        // TODO: Maybe(?) implement these
        public static AsyncTask operator |(AsyncTask a, AsyncTask b)
        {
            return a;
        }

        public static AsyncTask operator &(AsyncTask a, AsyncTask b)
        {
            return a;
        }
    }
}
