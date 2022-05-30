using SharpInit.Platform;
using SharpInit.Platform.Unix;
using SharpInit.Units;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Net.Sockets;
using Mono.Unix.Native;

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
                        var socket = Unit.SocketManager.CreateSocket(Unit, listen.Key, address);

                        if (socket == null)
                        {
                            return new TaskResult(this, ResultType.Failure, $"Could not create socket \"{address}\" of type {listen.Key}");
                        }

                        if (socket.AddressFamily == AddressFamily.InterNetwork || socket.AddressFamily == AddressFamily.InterNetworkV6) 
                        {
                            int opt_level = (int)(socket.AddressFamily == AddressFamily.InterNetwork ? SocketOptionLevel.IP : SocketOptionLevel.IPv6);

                            // IP_FREEBIND == 15
                            if (Unit.Descriptor.FreeBind)
                                socket.SetRawSocketOption(opt_level, 15, new ReadOnlySpan<byte>(new byte[] { 1 }));
                            
                            if (Unit.Descriptor.BindToDevice != default)
                                socket.SetRawSocketOption(opt_level, (int)UnixSocketOptionName.SO_BINDTODEVICE, Encoding.ASCII.GetBytes(Unit.Descriptor.BindToDevice));
                        }

                        if (socket.ProtocolType == ProtocolType.Tcp)
                        {
                            socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, Unit.Descriptor.NoDelay);

                            if (Unit.Descriptor.KeepAliveTimeSec != default)
                                socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, (int)Unit.Descriptor.KeepAliveTimeSec.TotalSeconds);

                            // TCP_KEEPCNT == 6
                            if (Unit.Descriptor.KeepAliveProbes != default)
                                socket.SetRawSocketOption((int)SocketOptionLevel.Tcp, 6, BitConverter.GetBytes(Unit.Descriptor.KeepAliveProbes));
                        }

                        if (socket.AddressFamily == AddressFamily.Unix)
                        {
                            if (Unit.Descriptor.SocketUser != default)
                            {
                                var group = Unit.Descriptor.SocketGroup;
                                UnixUserIdentifier identifier;

                                if (group == default)
                                    identifier = new UnixUserIdentifier(Unit.Descriptor.SocketUser);
                                else
                                    identifier = new UnixUserIdentifier(Unit.Descriptor.SocketUser, Unit.Descriptor.SocketGroup);
                                
                                if (identifier.GroupId >= 0 && identifier.UserId >= 0)
                                {
                                    var fileinfo = new Mono.Unix.UnixFileInfo(address);
                                    fileinfo.SetOwner(identifier.UserId, identifier.GroupId);
                                }
                            }

                            if (Unit.Descriptor.SocketMode != default)
                            {
                                var fileinfo = new Mono.Unix.UnixFileInfo(address);
                                fileinfo.FileAccessPermissions = (Mono.Unix.FileAccessPermissions)Unit.Descriptor.SocketMode;
                            }
                        }

                        if (socket.SocketType == SocketType.Seqpacket || socket.SocketType == SocketType.Stream)
                            socket.Listen();
                        Unit.SocketManager.AddSocket(new SocketWrapper(socket, Unit));
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
