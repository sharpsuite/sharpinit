using NLog;
using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace SharpInit.Ipc
{
    public delegate void OnMessageReceived(JsonSocketTunnel source, string message);
    public delegate void OnTunnelClosed(JsonSocketTunnel tunnel);

    /// <summary>
    /// Abstraction over the System.Net.Sockets.Socket Berkeley socket interface.
    /// </summary>
    public class JsonSocketTunnel
    {
        Logger Log = LogManager.GetCurrentClassLogger();

        public event OnMessageReceived MessageReceived;
        public event OnTunnelClosed TunnelClosed;

        public bool Closed { get; set; }
        public Socket Socket { get; set; }

        public JsonSocketTunnel(Socket socket)
        {
            Socket = socket;
        }

        public void StartMessageLoop()
        {
            new Thread(MessageLoop).Start();
        }

        public void Close()
        {
            if (Closed)
                return;

            Socket.Close();

            Closed = true;
            TunnelClosed?.Invoke(this);
        }

        private void MessageLoop()
        {
            while(!Closed)
            {
                try
                {
                    var message = Receive();

                    if (message == null || message.Length == 0)
                        continue;

                    MessageReceived?.Invoke(this, message);
                }
                catch (Exception ex)
                {
                    Log.Warn("Exception occurred in MessageLoop");
                    Log.Warn(ex);
                }
            }
        }

        public string Receive()
        {
            try
            {
                var length_buffer = new byte[4];
                var read = Socket.Receive(length_buffer);

                if (read != 4)
                    return null;

                if (BitConverter.ToUInt32(length_buffer, 0) > 0)
                {
                    var len = BitConverter.ToUInt32(length_buffer, 0);

                    if (len > 1024 * 1024) // for safety
                        throw new InvalidOperationException();

                    var message_buffer = new byte[len];
                    int index = 0;

                    DateTime read_start = DateTime.Now; // protect against slowloris-like attacks

                    while (index < len - 1)
                    {
                        try
                        {
                            read = Socket.Receive(message_buffer, index, (int)Math.Min(len, len - index), SocketFlags.None);
                            index += read;
                        }
                        catch
                        {
                            if ((read_start - DateTime.Now).TotalSeconds > 15)
                                throw new TimeoutException();
                        }
                    }

                    return Encoding.UTF8.GetString(message_buffer);
                }
                else
                    return null;
            }
            catch (SocketException ex) // this socket is probably closed now
            {
                Close();
                return null; // unblock anything waiting on Receive()
            }
            catch
            {
                throw;
            }
        }

        public void Send(byte[] data)
        {
            var len = data.Length;

            Socket.Send(BitConverter.GetBytes(len));
            Socket.Send(data);
        }
    }
}
