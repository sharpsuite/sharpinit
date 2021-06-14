using SharpInit.Units;
using Mono.Unix;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using NLog;

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

        public Socket CreateSocket(string property_name, string address)
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

            if (address.StartsWith('/')) 
            {
                address_family = AddressFamily.Unix; 
                protocol_type = ProtocolType.Unspecified;
                socket_ep = new UnixEndPoint(address);
            }

            if (address.StartsWith('@')) 
            {
                address_family = AddressFamily.Unix;
                protocol_type = ProtocolType.Unspecified;
                address = (char)0 + address.Substring(1);
                socket_ep = new UnixEndPoint(address);
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

            if (socket_ep == default)
            {
                return null;
            }

            var socket = new Socket(address_family, socket_type, protocol_type);
            socket.Bind(socket_ep);

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
                        throw;
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
                    socket.Unit.RaiseSocketActivated(socket);
                }
            }
        }
    }
}
