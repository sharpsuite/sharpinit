using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpInit.Tasks
{
    /// <summary>
    /// Container of per-instance task information.
    /// </summary>
    public class TaskContext
    {
        public Dictionary<string, object> Values { get; set; }

        public object this[string index] { get => Values.ContainsKey(index) ? Values[index] : null; set => Values[index] = value; }

        public T Get<T>(string key) => (T)this[key];

        public bool Has(string key) => Values.ContainsKey(key);

        public bool Has<T>(string key) => Values.ContainsKey(key) && Values[key].GetType() == typeof(T);

        public static TaskContext With<T>(string key, T value) => new TaskContext().With(key, value);

        public TaskContext() { Values = new Dictionary<string, object>(); }
    }

    public static class TaskContextExtensions
    {
        public static TaskContext With<T>(this TaskContext ctx, string key, T value) { ctx[key] = value; return ctx; }
    }
}
