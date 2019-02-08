using System;
using System.Collections.Generic;
using System.Text;

namespace SharpInit.Tasks
{
    public abstract class Task
    {
        public abstract string Type { get; }
        public abstract TaskResult Execute();
    }
}
