using SharpInit.Platform;
using SharpInit.Units;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Sockets;

namespace SharpInit.Tasks
{
    /// <summary>
    /// Starts a socket and associates it with the socket manager of a particular unit.
    /// </summary>
    public class CreateRegisteredSocketTask : Task
    {
        public override string Type => "create-registered-socket";
        public SocketUnit Unit { get; set; }

        /// <summary>
        /// Starts listening on a socket and associates it with the socket manager of <paramref name="unit"/>.
        /// </summary>
        /// <param name="unit">The Unit to associate the newly created socket with.</param>
        public CreateRegisteredSocketTask(SocketUnit unit)
        {
            Unit = unit;
        }

        public override TaskResult Execute(TaskContext context)
        {
            if (Unit == null)
            {
                return new TaskResult(this, ResultType.Failure, "Unit not specified.");
            }

            try
            {
                foreach (var listen in Unit.Descriptor.ListenStatements) 
                {
                    foreach (var address in listen.Value)
                    {
                        var socket = Unit.SocketManager.CreateSocket(listen.Key, address);

                        if (socket == null)
                        {
                            return new TaskResult(this, ResultType.Failure, $"Could not create socket \"{address}\" of type {listen.Key}");
                        }

                        socket.Listen();
                        Unit.SocketManager.AddSocket(new SocketWrapper(socket, Unit));
                    }
                }

                return new TaskResult(this, ResultType.Success);
            }
            catch (Exception ex)
            {
                return new TaskResult(this, ResultType.Failure, ex.Message);
            }
        }
    }
}
