using Newtonsoft.Json;
using NLog;
using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;

namespace SharpInit.Ipc
{
    /// <summary>
    /// Client to server IPC connection.
    /// </summary>
    public class IpcConnection : IpcInterface
    {
        Logger Log = LogManager.GetCurrentClassLogger();
        public JsonSocketTunnel Tunnel { get; set; }

        private Dictionary<int, ManualResetEvent> _waiting_for_response = new Dictionary<int, ManualResetEvent>();
        private Dictionary<int, IpcResult> _responses = new Dictionary<int, IpcResult>();

        public IpcConnection()
        {

        }

        public void Connect()
        {
            InitializeSocket();
            Socket.Connect(SocketEndPoint);
            Tunnel = new JsonSocketTunnel(Socket);

            Tunnel.MessageReceived += HandleLinkMessage;

            Tunnel.StartMessageLoop();
        }

        public void Disconnect()
        {
            foreach (var waiting in _waiting_for_response)
                waiting.Value.Set();

            if (Socket.Connected)            
                Socket.Close();
        }

        private void HandleLinkMessage(JsonSocketTunnel link, string msg)
        {
            try
            {
                var ipc_msg = IpcMessage.Deserialize(msg);

                if (!ipc_msg.IsResponse)
                    return;

                if (!_waiting_for_response.ContainsKey(ipc_msg.Id))
                    return;

                if(!string.IsNullOrWhiteSpace(ipc_msg.PayloadText))
                    _responses[ipc_msg.Id] = JsonConvert.DeserializeObject<IpcResult>(ipc_msg.PayloadText, SerializerSettings);

                _waiting_for_response[ipc_msg.Id].Set();
                _waiting_for_response.Remove(ipc_msg.Id);
            }
            catch (Exception ex)
            {
                Log.Warn("Exception occurred while trying to handle incoming IPC message");
                Log.Warn(ex);
            }
        }

        public IpcResult SendMessageWaitForReply(IpcMessage message)
        {
            var waiter = new ManualResetEvent(false);
            _waiting_for_response[message.Id] = waiter;

            SendMessage(message);

            if (!waiter.WaitOne(30000))
                throw new TimeoutException();

            return _responses.ContainsKey(message.Id) ? _responses[message.Id] : null;
        }

        public void SendMessage(IpcMessage message)
        {
            if (!Tunnel.Closed)
                Tunnel.Send(message.Serialize());
            else
                throw new Exception("IPC socket has been closed");
        }

        private JsonSerializerSettings _settings = new JsonSerializerSettings()
        {
            TypeNameHandling = TypeNameHandling.Auto
        };
    }
}
