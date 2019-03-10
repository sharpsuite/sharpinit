using Mono.Unix;
using Newtonsoft.Json;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;

namespace SharpInit.Ipc
{
    /// <summary>
    /// An implementation of IpcInterface that listens on the SharpInit IPC endpoint.
    /// Parses IPC requests, dispatches method calls, and responds with status information.
    /// </summary>
    public class IpcListener : IpcInterface
    {
        Logger Log = LogManager.GetCurrentClassLogger();
        public bool Running { get; set; }

        public List<JsonSocketTunnel> IncomingConnections = new List<JsonSocketTunnel>();
        private Thread ListenerThread;

        public void StartListening()
        {
            InitializeSocket();

            if(SocketEndPoint.AddressFamily == AddressFamily.Unix) // remove the socket file if it already exists
            {
                var unix_endpoint = SocketEndPoint as UnixEndPoint;
                var dir = Path.GetDirectoryName(unix_endpoint.Filename);

                if (File.Exists(unix_endpoint.Filename))
                    File.Delete(unix_endpoint.Filename);
                else if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
            }

            Socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            Socket.Bind(SocketEndPoint);
            Socket.Listen(10);

            if(SocketEndPoint.AddressFamily == AddressFamily.Unix && (Environment.GetEnvironmentVariable("SHARPINIT_IPC_FILE_WORLD_ACCESS") ?? "false") == "true") // set socket file to be readable by anyone
            {
                var unix_endpoint = SocketEndPoint as UnixEndPoint;
                var fileinfo = new UnixFileInfo(unix_endpoint.Filename);
                fileinfo.FileAccessPermissions = FileAccessPermissions.AllPermissions;
            }

            Running = true;
            ListenerThread = new Thread(ListenerLoop);
            ListenerThread.Start();
        }

        public void StopListening()
        {
            Running = false;

            Socket.Close();
        }
        
        private void ListenerLoop()
        {
            while (Socket.IsBound && Running)
            {
                try
                {
                    var incoming_socket = Socket.Accept();
                    var tunnel = new JsonSocketTunnel(incoming_socket);

                    tunnel.MessageReceived += HandleLinkMessage;
                    tunnel.TunnelClosed += HandleLinkClose;

                    tunnel.StartMessageLoop();
                    IncomingConnections.Add(tunnel);
                }
                catch (SocketException ex)
                {
                    // we're probably unbound
                    Log.Warn("SocketException in ListenerLoop, stopped listening");
                    Log.Warn(ex);
                    StopListening();
                }
                catch
                {
                    throw;
                }
            }
        }

        private void HandleLinkClose(JsonSocketTunnel tunnel)
        {
            IncomingConnections.Remove(tunnel);
        }

        private void HandleLinkMessage(JsonSocketTunnel tunnel, string message)
        {
            try
            {
                var ipc_message = IpcMessage.Deserialize(message);

                Log.Debug($"Received IPC message from {tunnel}");
                Log.Debug($"Calling {ipc_message.Endpoint} from {ipc_message.SourceName}");

                var ipc_function = IpcFunctionRegistry.GetFunction(ipc_message.Endpoint);
                int ipc_id = ipc_message.Id;

                var deserialized_payload = ipc_message.PayloadText.Length != 0 ? JsonConvert.DeserializeObject<object[]>(ipc_message.PayloadText, SerializerSettings) : null;

                var ipc_response = new IpcResult();

                try
                {
                    var ret = ipc_function.Execute(deserialized_payload);
                    ipc_response = new IpcResult(true, ret);
                }
                catch (TargetInvocationException ex)
                {
                    Log.Warn($"IPC function {ipc_function} threw an exception: {ex.InnerException.Message}");
                    Log.Warn(ex.InnerException);

                    ipc_response = new IpcResult(false, ex.InnerException.Message);
                }
                catch (Exception ex)
                {
                    Log.Error($"Unknown exception occurred while executing IPC function {ipc_function}");
                    Log.Error(ex);

                    ipc_response = new IpcResult(false, ex.Message);
                }

                tunnel.Send(new IpcMessage(ipc_message, Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(ipc_response, SerializerSettings))).Serialize());
            }
            catch (Exception ex)
            {
                Log.Warn("Exception occurred in IpcListener.HandleLinkMessage");
                Log.Warn(ex);
            }
        }
    }
}
