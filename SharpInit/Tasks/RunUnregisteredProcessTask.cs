using SharpInit.Platform;
using System;
using System.Collections.Generic;
using System.Text;

namespace SharpInit.Tasks
{
    /// <summary>
    /// Starts a process, optionally waits for it to exit before continuing execution.
    /// </summary>
    public class RunUnregisteredProcessTask : AsyncTask
    {
        public override string Type => "run-unregistered-process";
        public ProcessStartInfo ProcessStartInfo { get; set; }
        public IProcessHandler ProcessHandler { get; set; }
        public TimeSpan ExecutionTime = TimeSpan.Zero;

        /// <summary>
        /// Starts a process, optionally waits for it to exit before continuing execution.
        /// </summary>
        /// <param name="process_handler">The IProcessHandler that will start the process.</param>
        /// <param name="psi">The ProcessStartInfo that defines the parameters of the newly started process.</param>
        /// <param name="time">If -1, continue execution immediately after starting process. 
        /// Else, waits the process to exit for this many milliseconds. If the process does not exit in the given timeframe, 
        /// the process is killed, and a ResultType of Timeout is returned.</param>
        public RunUnregisteredProcessTask(IProcessHandler process_handler, ProcessStartInfo psi, int time = -1)
        {
            ProcessHandler = process_handler;
            ProcessStartInfo = psi;
            ExecutionTime = time == -1 ? TimeSpan.Zero : TimeSpan.FromMilliseconds(time);
        }

        public RunUnregisteredProcessTask(IProcessHandler process_handler, ProcessStartInfo psi, TimeSpan time = default) :
            this(process_handler, psi, time == default ? -1 : (int)time.TotalMilliseconds)
        {
            
        }

        public async override System.Threading.Tasks.Task<TaskResult> ExecuteAsync(TaskContext context)
        {
            if (ProcessStartInfo == null)
                return new TaskResult(this, ResultType.Failure, "No ProcessStartInfo supplied.");

            try
            {
                if (ProcessStartInfo?.Unit?.CGroup?.Exists == true)
                {
                    ProcessStartInfo.CGroup = ProcessStartInfo.Unit.CGroup;
                }

                var process = await ProcessHandler.StartAsync(ProcessStartInfo);

                if (ExecutionTime == TimeSpan.Zero)
                    return new TaskResult(this, ResultType.Success);
                else
                {
                    if (!(await process.WaitForExitAsync(ExecutionTime)))
                    {
                        process.Process.Kill();
                        return new TaskResult(this, ResultType.Timeout, $"The process {process.Id} did not exit in the given amount of time.");
                    }
                    else
                        return new TaskResult(this, ResultType.Success, $"pid {process.Id} exit code {process.ExitCode}");
                }
            }
            catch (Exception ex)
            {
                return new TaskResult(this, ResultType.Failure, ex.Message);
            }
        }
    }
}
