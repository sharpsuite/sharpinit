using Mono.Unix;
using SharpInit.Units;
using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using NLog.Fluent;

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
                
                if (Unit is ServiceUnit serviceUnit && serviceUnit.NotifySocket?.IsBound == true)
                {
                    sockets.Add(new SocketWrapper(serviceUnit.NotifySocket, Unit));
                    Runner.ServiceManager.NotifySocketManager.RemoveClient(serviceUnit.NotifyClient);
                }
                
                foreach (var socket in sockets)
                {
                    var isUnix = socket.Socket.LocalEndPoint is UnixEndPoint;
                    //socket.Socket.Disconnect(false);
                    try
                    {
                        Unit.SocketManager.RemoveSocket(socket);
                        
                        socket.Socket.Shutdown(System.Net.Sockets.SocketShutdown.Both);
                        socket.Socket.Close();
                    }
                    catch (Exception e)
                    {
                        Log.Warn($"Exception thrown while closing socket for unit {Unit.UnitName}");
                    }

                    if (isUnix && Unit.Descriptor is SocketUnitDescriptor socketDescriptor && socketDescriptor.RemoveOnExit)
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
                return new TaskResult(this, ResultType.Failure, ex);
            }
        }
    }
}
