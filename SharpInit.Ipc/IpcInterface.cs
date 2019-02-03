using Mono.Unix;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace SharpInit.Ipc
{
    /// <summary>
    /// Represents a platform-agnostic interface for controlling and automating SharpInit functionality.
    /// Under Linux, this represents a UNIX socket at /var/run/sharpinit/sharpinit.sock
    /// Under Windows, this represents a TCP socket to 127.0.0.1:9998
    /// </summary>
    public class IpcInterface
    {
        public static bool PlatformSupportsUnixSockets
        {
            get
            {
                if (_platform_unix_sockets_support.HasValue)
                    return _platform_unix_sockets_support.Value;

                _platform_unix_sockets_support = new bool?(
                    RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
                    RuntimeInformation.IsOSPlatform(OSPlatform.OSX));

                return _platform_unix_sockets_support.Value;
            }
        }
        private static bool? _platform_unix_sockets_support = new bool?();

        public IpcInterfaceMode InterfaceMode { get; set; }
        public Socket Socket { get; set; }
        public EndPoint SocketEndPoint { get; set; }
        
        public static JsonSerializerSettings SerializerSettings = new JsonSerializerSettings()
        {
            TypeNameHandling = TypeNameHandling.All
        };

        public IpcInterface()
        {
        }

        public void InitializeSocket()
        {
            if (InterfaceMode == IpcInterfaceMode.Unknown)
            {
                if (PlatformSupportsUnixSockets)
                {
                    InterfaceMode = IpcInterfaceMode.Unix;
                }
                else
                {
                    InterfaceMode = IpcInterfaceMode.TCP;
                }
            }

            switch (InterfaceMode)
            {
                case IpcInterfaceMode.TCP:
                    Socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    SocketEndPoint = new IPEndPoint(IPAddress.Loopback, 9095);
                    break;
                case IpcInterfaceMode.Unix:
                    Socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                    SocketEndPoint = new UnixEndPoint("/var/run/sharpinit/sharpinit.sock");
                    break;
            }
        }
    }

    public enum IpcInterfaceMode
    {
        Unknown, TCP, Unix
    }
}
