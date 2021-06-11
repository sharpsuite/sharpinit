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

        public TaskContext() { }
    }
}
