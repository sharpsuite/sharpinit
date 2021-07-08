﻿using System;
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
        public TaskRunner Runner => Execution.Runner;
        public ServiceManager ServiceManager => Execution.ServiceManager;
        public UnitRegistry Registry => Execution.Registry;

        public long Identifier { get; set; }

        public abstract string Type { get; }
        public abstract TaskResult Execute(TaskContext context);

        protected TaskResult ExecuteYielding(Task task, TaskContext context) => Execution.YieldExecute(task, context);
        protected TaskResult ExecuteYielding(TaskExecution exec) => Execution.YieldExecute(exec);

        public override string ToString()
        {
            if (Identifier > 0 && Identifier < 65536)
                return $"{Type}/{Identifier}";

            return $"{Type}/{Identifier,16:x16}";
        }
    }
}
