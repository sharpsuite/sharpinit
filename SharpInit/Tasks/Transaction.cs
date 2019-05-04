using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SharpInit.Tasks
{
    public class Transaction : Task
    {
        public override string Type => "transaction";
        public string Name { get; set; }
        public List<Task> Tasks = new List<Task>();
        public Task OnFailure { get; set; }
        public TransactionErrorHandlingMode ErrorHandlingMode { get; set; }

        public object Lock { get; set; }

        public Transaction()
        {

        }

        public Transaction(params Task[] tasks)
        {
            Add(tasks);
        }

        public Transaction(params IEnumerable<Task>[] tasks)
        {
            Add(tasks);
        }

        public Transaction(string name)
        {
            Name = name;
        }

        public Transaction(string name, params Task[] tasks)
        {
            Name = name;
            Add(tasks);
        }

        public Transaction(string name, params IEnumerable<Task>[] tasks)
        {
            Name = name;
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
            var lock_obj = Lock ?? new object();

            lock (lock_obj)
            {
                foreach (var task in Tasks)
                {
                    var result = task.Execute();

                    if (ErrorHandlingMode != TransactionErrorHandlingMode.Ignore &&
                        result.Type != ResultType.Success &&
                        !result.Type.HasFlag(ResultType.Ignorable))
                    {
                        if (OnFailure != null)
                            OnFailure.Execute();

                        // fatal failure
                        return result;
                    }
                    else if (result.Type == ResultType.StopExecution)
                    {
                        break;
                    }
                }
            }

            return new TaskResult(this, ResultType.Success);
        }

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
