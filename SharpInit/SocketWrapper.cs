﻿using SharpInit.Units;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace SharpInit
{
    /// <summary>
    /// Associates a Socket with a Unit.
    /// </summary>
    public class SocketWrapper
    {
        public Socket Socket { get; set; }
        public Unit Unit { get; set; }

        public SocketWrapper(Socket socket, Unit unit)
        {
            Socket = socket;
            Unit = unit;
        }
    }
}
