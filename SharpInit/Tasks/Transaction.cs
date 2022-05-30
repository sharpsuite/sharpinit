using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using NLog;

namespace SharpInit.Tasks
{
    /// <summary>
    /// A Task that contains other Tasks. The child tasks execute sequentially, and execution can 
    /// stop depending on the TaskResult returned by each child Task.
    /// </summary>
    public class Transaction : AsyncTask
    {
        static Logger Log = LogManager.GetCurrentClassLogger();

        public override string Type => "transaction";
        public string Name { get; set; }
        public List<Task> Tasks = new List<Task>();
        public Task OnFailure { get; set; }
        public Task OnTimeout { get; set; }
        public Task OnSkipped { get; set; }

        public TaskContext Context { get; set; }

        /// <summary>
        /// The error handling mode of this transaction. Set to Ignore if execution should continue upon the failure of a child Task.
        /// </summary>
        public TransactionErrorHandlingMode ErrorHandlingMode { get; set; }

        public TransactionSynchronizationMode TransactionSynchronizationMode { get; set; }
        public bool Cancelled { get; set; }

        public object Lock { get; set; }

        /// <summary>
        /// Creates an empty Transaction.
        /// </summary>
        public Transaction()
        {

        }

        /// <summary>
        /// Creates an unnamed Transaction with the given child Tasks.
        /// </summary>
        /// <param name="tasks">The tasks that become immediate children of this transaction.</param>
        public Transaction(params Task[] tasks)
        {
            Add(tasks);
        }

        /// <summary>
        /// Creates an unnamed Transaction with the given child Tasks.
        /// </summary>
        /// <param name="tasks">The tasks that become immediate children of this transaction.</param>
        public Transaction(params IEnumerable<Task>[] tasks)
        {
            Add(tasks);
        }

        /// <summary>
        /// Creates an empty, named Transaction.
        /// </summary>
        /// <param name="name">The name of this transaction.</param>
        public Transaction(string name)
        {
            Name = name;
        }

        /// <summary>
        /// Creates a named Transaction with the given child Tasks.
        /// </summary>
        /// <param name="name">The name of this transaction.</param>
        /// <param name="tasks">The tasks that become immediate children of this transaction.</param>
        public Transaction(string name, params Task[] tasks)
        {
            Name = name;
            Add(tasks);
        }

        /// <summary>
        /// Creates a named Transaction with the given child Tasks.
        /// </summary>
        /// <param name="name">The name of this transaction.</param>
        /// <param name="tasks">The tasks that become immediate children of this transaction.</param>
        public Transaction(string name, params IEnumerable<Task>[] tasks)
        {
            Name = name;
            Add(tasks);
        }

        /// <summary>
        /// Adds a task to the transaction.
        /// </summary>
        /// <param name="task">The task to be added.</param>
        public void Add(Task task)
        {
            Tasks.Add(task);
        }

        /// <summary>
        /// Adds a range of tasks to the transaction.
        /// </summary>
        /// <param name="tasks">The tasks to be added.</param>
        public void Add(params IEnumerable<Task>[] tasks)
        {
            Tasks.AddRange(tasks.SelectMany(t => t));
        }

        /// <summary>
        /// Executes the transaction, and returns the result.
        /// </summary>
        /// <returns>A TaskResult that has the ResultType Success if all tasks executed successfully, 
        /// or the TaskResult returned by a failed task, depending on ErrorHandlingMode.</returns>
        public async override System.Threading.Tasks.Task<TaskResult> ExecuteAsync(TaskContext context = null)
        {
            Context = context ?? new TaskContext();
            var lock_obj = Lock ?? new object();

            IEnumerable<IEnumerable<Task>> sub_transactions = null;

            if (TransactionSynchronizationMode == TransactionSynchronizationMode.Implicit)
                sub_transactions = Tasks.Select(t => new [] {t});
            else if (TransactionSynchronizationMode == TransactionSynchronizationMode.Explicit)
                sub_transactions = Tasks.Partition(t => t is SynchronizationTask);

            foreach (var task_group in sub_transactions)
            {
                if (Cancelled)
                    break;
                
                var executions_list = new List<System.Threading.Tasks.Task<TaskResult>>();

                foreach (var task in task_group)
                    executions_list.Add(Runner.ExecuteAsync(task, Context));
                
                var executions = executions_list.ToArray();
                await System.Threading.Tasks.Task.WhenAll(executions);

                foreach (var execution in executions)
                {
                    var result = execution.Result;

                    if (result.Type.HasFlag(ResultType.Skipped))
                    {
                        if (OnSkipped != null)
                            ExecuteBlocking(OnSkipped, Context);
                    }

                    if (!result.Type.HasFlag(ResultType.Success) &&
                        !result.Type.HasFlag(ResultType.Ignorable) &&
                        !result.Type.HasFlag(ResultType.StopExecution))
                    {
                        Context["failure"] = result;

                        if (result.Type.HasFlag(ResultType.Timeout))
                        {
                            if (OnTimeout != null)
                                ExecuteBlocking(OnTimeout, Context);
                        }
                        else if (result.Type.HasFlag(ResultType.Failure))
                        {
                            if (OnFailure != null)
                                ExecuteBlocking(OnFailure, Context);
                        }
                        
                        if (ErrorHandlingMode != TransactionErrorHandlingMode.Ignore)
                            return result;
                    }
                    
                    if (result.Type.HasFlag(ResultType.StopExecution))
                    {
                        var result_to_propagate = result.Type ^ ResultType.StopExecution;

                        if (result_to_propagate == (ResultType)0 || result_to_propagate == ResultType.Ignorable)
                            result_to_propagate = ResultType.Success;

                        return new TaskResult(this, result_to_propagate);
                    }
                }
            }

            return new TaskResult(this, ResultType.Success);
        }

        public void Append(Task task) => Tasks.Add(task);

        public void Prepend(Task task) => Tasks.Insert(0, task);

        /// <summary>
        /// Generates a textual tree that describes the child tasks of this transaction, with an optionally highlighted task. 
        /// Used for diagnostics.
        /// </summary>
        /// <param name="indent">Always set this to 0.</param>
        /// <param name="highlighted">The task to be highlighted.</param>
        /// <param name="highlight_text">The highlight text to use for the highlighted task.</param>
        /// <returns></returns>
        public string GenerateTree(int indent = 0, Task highlighted = null, string highlight_text = "highlighted task")
        {
            var sb = new StringBuilder();

            var indent_str = new string(' ', indent);
            sb.AppendLine($"{indent_str}+ {(string.IsNullOrWhiteSpace(Name) ? "(unnamed transaction)" : Name)}");

            indent += 2;
            indent_str = new string(' ', indent);

            foreach (var task in Tasks)
            {
                if (task is Transaction)
                {
                    sb.Append((task as Transaction).GenerateTree(indent, highlighted, highlight_text));
                }
                else if (task == highlighted)
                {
                    sb.AppendLine($"{indent_str}{task} <----- {highlight_text}");
                }
                else
                {
                    sb.AppendLine($"{indent_str}{task}");
                }
            }

            return sb.ToString();
        }

        public override string ToString()
        {
            return Name;
        }
    }

    public static class IEnumerablePartitionHelper
    {
        public static IEnumerable<IEnumerable<T>> Partition<T>(this IEnumerable<T> set, Predicate<T> partition_predicate)
        {
            var enumerator = set.GetEnumerator();
            while (enumerator.MoveNext())
                yield return GetNextPartition(enumerator, partition_predicate);
        }

        public static IEnumerable<T> GetNextPartition<T>(IEnumerator<T> set, Predicate<T> partition_predicate)
        {
            do
            {
                if (!partition_predicate(set.Current))
                    yield return set.Current;
            } while (set.MoveNext() && !partition_predicate(set.Current));
        }
    }

    public enum TransactionErrorHandlingMode
    {
        Fail,
        Ignore
    }

    public enum TransactionSynchronizationMode
    {
        Implicit,
        Explicit
    }
}
