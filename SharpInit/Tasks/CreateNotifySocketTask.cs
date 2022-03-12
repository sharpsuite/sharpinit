using SharpInit.Platform;
using SharpInit.Platform.Unix;
using SharpInit.Units;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Sockets;
using Mono.Unix.Native;

namespace SharpInit.Tasks
{
    /// <summary>
    /// Starts a socket to be used for sd_notify.
    /// </summary>
    public class CreateNotifySocketTask : Task
    {
        public override string Type => "create-sd-notify-socket";
        public ServiceUnit Unit { get; set; }

        /// <summary>
        /// Creates the notification socket for a given service unit.
        /// </summary>
        /// <param name="unit">The Unit to associate the newly created socket with.</param>
        public CreateNotifySocketTask(ServiceUnit unit)
        {
            Unit = unit;
        }

        public override TaskResult Execute(TaskContext context)
        {
            try
            {
                if (!PlatformUtilities.CurrentlyOn("unix"))
                    return new TaskResult(this, ResultType.Failure,
                        "Notify sockets are supported on Unix platforms only.");

                var socketAddress =
                    $"/var/run/sharpinit/notify/{Unit.UnitName}-{(long) (DateTime.UtcNow - DateTime.UnixEpoch).TotalMilliseconds}";
                var socket = ServiceManager.SocketManager.CreateSocket(Unit, "Datagram", socketAddress);
                
                Syscall.setsockopt(socket.Handle.ToInt32(), UnixSocketProtocol.SOL_SOCKET,
                    UnixSocketOptionName.SO_PASSCRED, 1);
                
                var client = new NotifyClient(Unit, socket.Handle.ToInt32());

                Runner.ServiceManager.NotifySocketManager.AddClient(client);
                Unit.NotifyClient = client;
                Unit.NotifySocket = socket;
                
                return new TaskResult(this, ResultType.Success);
            }
            catch (Exception e)
            {
                return new TaskResult(this, ResultType.Failure, e);
            }
        }
    }
}