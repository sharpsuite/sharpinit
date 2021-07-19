using System;
using SharpInit.Units;

namespace SharpInit.Tasks
{
    public class CheckProcessExitCodeTask : AsyncTask
    {
        public override string Type => "check-process-exit-code";
        public string ContextKey { get; set; }
        public Func<int, ResultType> ExitCodeDelegate { get; set; }
        public TimeSpan Timeout { get; set; }

        public CheckProcessExitCodeTask(string key, Func<int, ResultType> exit_code_delegate, TimeSpan timeout)
        {
            ContextKey = key;
            ExitCodeDelegate = exit_code_delegate;
            Timeout = timeout;
        }

        public CheckProcessExitCodeTask(string key, TimeSpan timeout) :
            this(key, exit_code => exit_code != 0 ? ResultType.Failure : ResultType.Success, timeout)
        {
            
        }

        public CheckProcessExitCodeTask(string key, Func<int, ResultType> exit_code_delegate)
            : this(key, exit_code_delegate, TimeSpan.Zero)
        {
            
        }

        public CheckProcessExitCodeTask(string key) :
            this(key, TimeSpan.Zero)
        {

        }

        public async override System.Threading.Tasks.Task<TaskResult> ExecuteAsync(TaskContext context)
        {
            try
            {
                if (!context.Has<ProcessInfo>(ContextKey))
                    return new TaskResult(this, ResultType.Failure, $"Could not find process with key \"{ContextKey}\" in task context");
                
                var process = context.Get<ProcessInfo>(ContextKey);
                
                if (!process.HasExited)
                {
                    if (Timeout > TimeSpan.Zero)
                    {
                        if (!(await process.WaitForExitAsync(Timeout)))
                        {
                            return new TaskResult(this, ResultType.Timeout, $"pid {process.Id} did not exit in time");
                        }
                    }
                    else
                    {
                        return new TaskResult(this, ResultType.Failure, $"pid {process.Id} still running, and no timeout given");
                    }
                }

                var exit_code = process.ExitCode;
                return new TaskResult(this, ExitCodeDelegate(exit_code));
            }
            catch (Exception ex)
            {
                return new TaskResult(this, ResultType.Failure | ResultType.StopExecution, ex.Message);
            }
        }
    }
}