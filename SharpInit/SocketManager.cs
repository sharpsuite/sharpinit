using SharpInit.Units;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace SharpInit
{
    public delegate void OnSocketActivation(Unit unit, Socket socket);

    public class SocketManager
    {
        List<SocketWrapper> Sockets = new List<SocketWrapper>();

        public SocketManager()
        {

        }

        public void AddSocket(SocketWrapper socket)
        {
            Sockets.Add(socket);
        }

        public bool RemoveSocket(SocketWrapper socket)
        {
            return Sockets.Remove(socket);
        }

        private void Select()
        {
            var list = Sockets.Select(w => w.Socket).ToList();
            Socket.Select(list, null, null, 1000);

            foreach (var socket in Sockets)
            {
                if (list.Contains(socket.Socket))
                {
                    socket.Unit.RaiseSocketActivated(socket.Socket);
                }
            }
        }
    }
}
