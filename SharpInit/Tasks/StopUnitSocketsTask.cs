using Mono.Unix;
using SharpInit.Units;
using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;

namespace SharpInit.Tasks
{
    /// <summary>
    /// Stops all sockets registered under a unit.
    /// </summary>
    public class StopUnitSocketsTask : Task
    {
        public override string Type => "stop-sockets-by-unit";
        public Unit Unit { get; set; }

        /// <summary>
        /// Stops all sockets registered under a unit.
        /// </summary>
        /// <param name="unit">The unit that owns the sockets.</param>
        public StopUnitSocketsTask(Unit unit)
        {
            Unit = unit;
        }

        public override TaskResult Execute(TaskContext context)
        {
            try 
            {
                var sockets = Unit.SocketManager.GetSocketsByUnit(Unit).ToList();

                foreach (var socket in sockets) 
                {
                    //socket.Socket.Disconnect(false);
                    Unit.SocketManager.RemoveSocket(socket);
                    socket.Socket.Shutdown(System.Net.Sockets.SocketShutdown.Both);
                    socket.Socket.Close();

                    if (socket.Socket.LocalEndPoint is UnixEndPoint && (Unit.Descriptor as SocketUnitDescriptor).RemoveOnExit)
                    {
                        try
                        {
                            var path = (socket.Socket.LocalEndPoint as UnixEndPoint).Filename;
                            UnixFileSystemInfo.GetFileSystemEntry(path).Delete();
                        }
                        catch {}
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
