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
    public class Transaction : Task
    {
        static Logger Log = LogManager.GetCurrentClassLogger();

        public override string Type => "transaction";
        public string Name { get; set; }
        public List<Task> Tasks = new List<Task>();
        public Task OnFailure { get; set; }
        public Task OnTimeout { get; set; }

        public TaskContext Context { get; set; }

        /// <summary>
        /// The error handling mode of this transaction. Set to Ignore if execution should continue upon the failure of a child Task.
        /// </summary>
        public TransactionErrorHandlingMode ErrorHandlingMode { get; set; }

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
        public override TaskResult Execute(TaskContext context = null)
        {
            Context = context ?? new TaskContext();
            var lock_obj = Lock ?? new object();

            Log.Info($"{Execution} is {this}");

            lock (lock_obj)
            {
                foreach (var task in Tasks)
                {
                    var execution = Runner.Register(task, Context);
                    var result = ExecuteYielding(execution);

                    if (result.Type != ResultType.Success &&
                        !result.Type.HasFlag(ResultType.Ignorable))
                    {
                        Context["failure"] = result;

                        if (result.Type.HasFlag(ResultType.Timeout))
                        {
                            if (OnTimeout != null)
                                ExecuteYielding(OnTimeout, Context);
                        }
                        else if (OnFailure != null)
                            ExecuteYielding(OnFailure, Context);
                        
                        if (ErrorHandlingMode != TransactionErrorHandlingMode.Ignore)
                            return result;
                    }
                    
                    if (result.Type.HasFlag(ResultType.StopExecution))
                    {
                        break;
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
                    sb.AppendLine($"{indent_str}{task.Type} <----- {highlight_text}");
                }
                else
                {
                    sb.AppendLine($"{indent_str}{task.Type}");
                }
            }

            return sb.ToString();
        }

        public override string ToString()
        {
            return Name;
        }
    }

    public enum TransactionErrorHandlingMode
    {
        Fail,
        Ignore
    }
}
