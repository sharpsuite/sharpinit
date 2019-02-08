using System;
using System.Collections.Generic;
using System.Text;

namespace SharpInit.Tasks
{
    public class TaskResult
    {
        public Task Task { get; set; }
        public ResultType Type { get; set; }
        public string Message { get; set; }

        public TaskResult(Task task, ResultType result, string msg = null)
        {
            Task = task;
            Type = result;
            Message = msg;
        }
    }

    public enum ResultType
    {
        Failure = 0x1,
        Success = 0x2,
        Ignorable = 0x4,
        SoftFailure = Failure | Ignorable,
        Timeout = Failure | 0x8
    }
}
