using SharpInit.Units;
using SharpInit.Platform;
using Mono.Unix;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.IO;

using NLog;
using SharpInit.Platform.Unix;

namespace SharpInit
{
    public delegate void OnSocketActivation(SocketWrapper wrapper);

    public class SocketManager
    {
        Logger Log = LogManager.GetCurrentClassLogger();

        Dictionary<SocketWrapper, Unit> IgnoredSockets = new Dictionary<SocketWrapper, Unit>();
        List<SocketWrapper> Sockets = new List<SocketWrapper>();

        public SocketManager()
        {

        }

        public Socket CreateSocket(Unit unit, string property_name, string address)
        {
            SocketType socket_type = SocketType.Unknown;
            ProtocolType protocol_type = ProtocolType.Unknown;
            AddressFamily address_family = AddressFamily.Unknown;
            EndPoint socket_ep = default;

            switch (property_name) 
            {
                case "Stream":
                    socket_type = SocketType.Stream;
                    protocol_type = ProtocolType.Tcp;
                    break;
                case "Datagram":
                    socket_type = SocketType.Dgram;
                    protocol_type = ProtocolType.Udp;
                    break;
                default:
                    socket_type = SocketType.Unknown;
                    break;
            }

            if (int.TryParse(address, out int port)) 
            {
                address_family = AddressFamily.InterNetwork;
                socket_ep = new IPEndPoint(IPAddress.Any, port);
            }

            if (address.Contains(':')) 
            {
                var parts = address.Split(':');

                if (IPAddress.TryParse(parts[0], out IPAddress ip_addr) &&
                    int.TryParse(parts[1], out port)) 
                {
                    socket_ep = new IPEndPoint(ip_addr, port);
                    address_family = socket_ep.AddressFamily;
                }
            }

            if (address.StartsWith('/')) 
            {
                if (!PlatformUtilities.CurrentlyOn("unix"))
                {
                    throw new Exception($"Socket address \"{address}\" is invalid on non-Unix platforms");
                }

                address_family = AddressFamily.Unix; 
                protocol_type = ProtocolType.Unspecified;
                socket_ep = new UnixEndPoint(address);

                var dir = Path.GetDirectoryName(address);

                if (!Directory.Exists(dir))
                {
                    var discarded_segments = new List<string>();
                    
                    while (!Directory.Exists(dir) && Path.GetDirectoryName(dir) != dir) 
                    {
                        discarded_segments.Add(Path.GetFileName(dir));
                        dir = Path.GetDirectoryName(dir);
                    }
                    
                    discarded_segments.Reverse();

                    foreach (var segment in discarded_segments)
                    {
                        var concatted = dir + "/" + segment;
                        Directory.CreateDirectory(concatted);
                        var di = new UnixDirectoryInfo(concatted);
                        if (unit is SocketUnit socketUnit)
                        {
                            di.FileAccessPermissions =
                                (FileAccessPermissions) socketUnit.Descriptor.DirectoryMode;
                        }
                        else
                        {
                            di.FileAccessPermissions =
                                FileAccessPermissions.UserReadWriteExecute |
                                FileAccessPermissions.GroupReadWriteExecute |
                                FileAccessPermissions.OtherRead | FileAccessPermissions.OtherExecute;
                        }

                        if (unit is ServiceUnit serviceUnit)
                        {
                            var descriptor = serviceUnit.Descriptor;
                            IUserIdentifier userInfo = null;
                            if (!string.IsNullOrWhiteSpace(descriptor.User) && !string.IsNullOrWhiteSpace(descriptor.Group))
                            {
                                userInfo = PlatformUtilities.GetImplementation<IUserIdentifier>(descriptor.User, descriptor.Group);
                            }
                            else if (!string.IsNullOrWhiteSpace(descriptor.User))
                            {
                                userInfo = PlatformUtilities.GetImplementation<IUserIdentifier>(descriptor.User);
                            }
                            else if (!string.IsNullOrWhiteSpace(descriptor.Group))
                            {
                                userInfo = PlatformUtilities.GetImplementation<IUserIdentifier>(null, descriptor.Group);
                            }

                            if (userInfo != null)
                            {
                                di.SetOwner(userInfo.Username, userInfo.Group);
                            }
                        }

                        dir = concatted;
                    }
                }
            }

            if (address.StartsWith('@')) 
            {
                if (!PlatformUtilities.CurrentlyOn("unix"))
                {
                    throw new Exception($"Socket address \"{address}\" is invalid on non-Unix platforms");
                }
                
                address_family = AddressFamily.Unix;
                protocol_type = ProtocolType.Unspecified;
                address = (char)0 + address.Substring(1);
                socket_ep = new UnixEndPoint(address);
            }

            if (socket_ep == default)
            {
                return null;
            }

            var socket = new Socket(address_family, socket_type, protocol_type);

            try 
            {
                socket.Bind(socket_ep);
            } 
            catch (Exception ex)
            {
                throw new Exception($"Could not bind to socket \"{address}\" of type {property_name}", ex);
            }

            return socket;
        }

        public void IgnoreSocket(SocketWrapper socket, Unit target_unit)
        {
            if (Sockets.Contains(socket) && !IgnoredSockets.ContainsKey(socket))
            {
                Log.Debug($"Ignoring socket {socket.Unit.UnitName} because of activated unit {target_unit.UnitName}");
                IgnoredSockets[socket] = target_unit;
            }
        }

        public void UnignoreSocket(SocketWrapper socket)
        {
            if (Sockets.Contains(socket) && IgnoredSockets.ContainsKey(socket))
            {
                Log.Debug($"Unignoring socket {socket.Unit.UnitName}");
                IgnoredSockets.Remove(socket);
            }
        }

        public void UnignoreSocketsByUnit(Unit unit)
        {
            if (!IgnoredSockets.Values.Contains(unit))
            {
                return;
            }

            var sockets_to_unignore = new List<SocketWrapper>();

            foreach (var pair in IgnoredSockets)
            {
                if (pair.Value == unit)
                {
                    sockets_to_unignore.Add(pair.Key);
                }
            }

            foreach (var socket in sockets_to_unignore)
            {
                UnignoreSocket(socket);
            }
        }

        public void AddSocket(SocketWrapper socket)
        {
            Sockets.Add(socket);
        }

        public bool RemoveSocket(SocketWrapper socket)
        {
            return Sockets.Remove(socket);
        }

        public IEnumerable<SocketWrapper> GetSocketsByUnit(Unit unit) 
        {
            return Sockets.Where(wrapper => wrapper.Unit == unit);
        }

        public void StartSelectLoop()
        {
            new Thread(new ThreadStart(delegate 
            {
                while (true)
                {
                    try
                    {
                        while (!Sockets.Any()) 
                        {
                            Thread.Sleep(10);
                        }

                        Select();
                    }
                    catch (Exception ex)
                    {
                        Log.Warn(ex);
                    }
                }
            })).Start();
        }

        private void Select()
        {
            var list = Sockets.Where(w => !IgnoredSockets.ContainsKey(w)).Select(w => w.Socket).ToList();

            if (list.Count == 0)
            {
                Thread.Sleep(10);
                return;
            }

            Socket.Select(list, null, null, 1000);

            foreach (var socket in Sockets)
            {
                if (list.Contains(socket.Socket))
                {
                    if (socket.Unit is SocketUnit socketUnit)
                        socketUnit.RaiseSocketActivated(socket);
                }
            }
        }
    }
}
