using System;

using SharpInit.Platform.Unix;

namespace SharpInit.Tasks
{
    public class DelegateTask : Task
    {
        private string _type;
        public override string Type => _type;

        private Action Action { get; set; }
        public ResultType ResultOnException { get; set; }

        public DelegateTask(Action action, string type = "delegate")
        {
            _type = type;
            Action = action;
            ResultOnException = ResultType.Failure;
        }

        public override TaskResult Execute(TaskContext context)
        {
            try
            {
                Action();
                return new TaskResult(this, ResultType.Success);
            }
            catch (Exception ex)
            {
                return new TaskResult(this, ResultOnException, ex.Message);
            }
        }
    }
}