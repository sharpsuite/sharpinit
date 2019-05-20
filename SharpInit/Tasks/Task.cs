using System;
using System.Collections.Generic;
using System.Text;

namespace SharpInit.Tasks
{
    /// <summary>
    /// A generalized unit of work, used to build transactions.
    /// </summary>
    public abstract class Task
    {
        public abstract string Type { get; }
        public abstract TaskResult Execute();
    }
}
