using SharpInit.Units;
using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using NLog;

namespace SharpInit.Tasks
{
    /// <summary>
    /// Updates the activation time of a unit.
    /// </summary>
    public class WaitForNotifySocketTask : AsyncTask
    {
        public Logger Log = LogManager.GetCurrentClassLogger();
        
        public override string Type => "wait-for-notify";
        public ServiceUnit Unit { get; set; }
        public int Timeout { get; set; }

        /// <summary>
        /// Wait for the service process to signal readiness over sd_notify.
        /// </summary>
        /// <param name="name">The service whose notify socket we should be waiting on.</param>
        /// <param name="timeout">The maximum amount of time to wait. -1 is indefinitely.</param>
        public WaitForNotifySocketTask(ServiceUnit unit, int timeout = -1)
        {
            Unit = unit;
            Timeout = timeout;
        }

        public async override System.Threading.Tasks.Task<TaskResult> ExecuteAsync(TaskContext context)
        {
            try
            {
                var budget = TimeSpan.FromMilliseconds(Timeout);
                
                var start = Program.ElapsedSinceStartup();
                var end = start + budget;

                while (Program.ElapsedSinceStartup() < end)
                {
                    if (!await Unit.NotifyClient.WaitOneAsync(end - Program.ElapsedSinceStartup()))
                        break;
                    
                    var message = Unit.NotifyClient.DequeueMessage().Contents.Trim();
                    if (message == "READY=1")
                        return new TaskResult(this, ResultType.Success);
                }
                
                return new TaskResult(this, ResultType.Failure | ResultType.Timeout);
            }
            catch (Exception ex)
            {
                return new TaskResult(this, ResultType.Failure, ex.Message);
            }
        }
    }
}