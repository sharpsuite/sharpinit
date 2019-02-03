using Newtonsoft.Json;
using NLog;
using System;
using System.Collections.Generic;
using System.Net.Sockets;
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

            Socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            Socket.Bind(SocketEndPoint);
            Socket.Listen(10);

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

                try
                {
                    var ret = ipc_function.Execute(deserialized_payload);
                    var ipc_response = new IpcResult(true, ret);

                    tunnel.Send(new IpcMessage(ipc_message, Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(ipc_response, SerializerSettings))).Serialize());
                }
                catch (Exception ex)
                {
                    Log.Warn($"Exception occurred while executing IPC function {ipc_function}");
                    Log.Warn(ex);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Exception occurred in IpcListener.HandleLinkMessage");
                Log.Warn(ex);
            }
        }
    }
}
