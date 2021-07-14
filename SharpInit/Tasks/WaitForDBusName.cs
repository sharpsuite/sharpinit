using SharpInit.Units;
using System;
using System.Collections.Generic;
using System.Text;

namespace SharpInit.Tasks
{
    /// <summary>
    /// Updates the activation time of a unit.
    /// </summary>
    public class WaitForDBusName : AsyncTask
    {
        public override string Type => "wait-for-dbus-name";
        public string BusName { get; set; }
        public int Timeout { get; set; }

        /// <summary>
        /// Wait for a DBus name to be acquired.
        /// </summary>
        /// <param name="name">The bus name to wait on.</param>
        /// <param name="timeout">The maximum amount of time to wait. -1 is indefinitely.</param>
        public WaitForDBusName(string name, int timeout = -1)
        {
            BusName = name;
            Timeout = timeout;
        }

        public async override System.Threading.Tasks.Task<TaskResult> ExecuteAsync(TaskContext context)
        {
            if (ServiceManager.DBusManager.Connection != null)
            {
                var result = await ServiceManager.DBusManager.WaitForBusName(BusName, Timeout);

                if (!result)
                    return new TaskResult(this, ResultType.Timeout, $"Bus name {BusName} did not appear in the given timeout.");
            }

            return new TaskResult(this, ResultType.Success);
        }
    }
}
