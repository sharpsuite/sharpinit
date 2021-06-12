using SharpInit.Units;
using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;

namespace SharpInit.Tasks
{
    /// <summary>
    /// Temporarily suspends activation for all sockets registered under a unit.
    /// </summary>
    public class IgnoreSocketsTask : Task
    {
        public override string Type => "ignore-sockets";
        public SocketUnit SocketUnit { get; set; }

        public Unit TargetUnit { get; set; }

        /// <summary>
        /// Temporarily suspends activation for all sockets registered under a unit.
        /// </summary>
        /// <param name="socket_unit">The unit that owns the sockets.</param>
        /// <param name="socket_unit">The unit that is activated and currently handles the sockets.</param>
        public IgnoreSocketsTask(SocketUnit socket_unit, Unit target_unit)
        {
            SocketUnit = socket_unit;
            TargetUnit = target_unit;
        }

        public override TaskResult Execute(TaskContext context)
        {
            var sockets = SocketUnit.SocketManager.GetSocketsByUnit(SocketUnit).ToList();

            foreach (var socket in sockets) 
            {
                SocketUnit.SocketManager.IgnoreSocket(socket, TargetUnit);
            }

            return new TaskResult(this, ResultType.Success);
        }
    }
}
