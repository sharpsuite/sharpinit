using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpInit.Units
{
    public class SocketUnitDescriptor : ExecUnitDescriptor
    {
        [UnitProperty("Socket/@", UnitPropertyType.StringList)]
        public List<string> ListenStream { get; set; }
        [UnitProperty("Socket/@", UnitPropertyType.StringList)]
        public List<string> ListenDatagram { get; set; }
        [UnitProperty("Socket/@", UnitPropertyType.StringList)]
        public List<string> ListenSequentialPacket { get; set; }

        [UnitProperty("Socket/@", UnitPropertyType.StringList)]
        public List<string> ListenFIFO { get; set; }
        [UnitProperty("Socket/@", UnitPropertyType.StringList)]
        public List<string> ListenSpecial { get; set; }
        [UnitProperty("Socket/@", UnitPropertyType.StringList)]
        public List<string> ListenNetlink { get; set; }
        [UnitProperty("Socket/@", UnitPropertyType.StringList)]
        public List<string> ListenMessageQueue { get; set; }
        [UnitProperty("Socket/@", UnitPropertyType.StringList)]
        public List<string> ListenUSBFunction { get; set; }

        [UnitProperty("Socket/@", UnitPropertyType.Int)]

        public int Backlog { get; set; }

        [UnitProperty("Socket/@", UnitPropertyType.Bool)]
        public bool FreeBind { get; set; }

        [UnitProperty("Socket/@")]
        public string BindToDevice { get; set; }

        [UnitProperty("Socket/@")]
        public string SocketUser { get; set; }
        [UnitProperty("Socket/@")]
        public string SocketGroup { get; set; }

        [UnitProperty("Socket/@", UnitPropertyType.IntOctal, 438)] // 0666 in decimal
        public int SocketMode { get; set; }
        [UnitProperty("Socket/@", UnitPropertyType.IntOctal, 493)] // 0755 in decimal
        public int DirectoryMode { get; set; }

        [UnitProperty("Socket/@", UnitPropertyType.Bool, false)]
        public bool Accept { get; set; }
        public bool Writable { get; set; }

        public bool FlushPending { get; set; }
        public int MaxConnections { get; set; }
        public int MaxConnectionsPerSource { get; set; }
        [UnitProperty("Socket/@", UnitPropertyType.Bool)]
        public bool KeepAlive { get; set; }
        [UnitProperty("Socket/@", UnitPropertyType.Time)]
        public TimeSpan KeepAliveTimeSec { get; set; }
        [UnitProperty("Socket/@", UnitPropertyType.Int)]
        public int KeepAliveProbes { get; set; }
        [UnitProperty("Socket/@", UnitPropertyType.Bool)]
        public bool NoDelay { get; set; }
        public int Priority { get; set; }
        public int DeferAcceptSec { get; set; }
        [UnitProperty("Socket/@", UnitPropertyType.Int)]
        public int ReceiveBuffer { get; set; }
        [UnitProperty("Socket/@", UnitPropertyType.Int)]
        public int SendBuffer { get; set; }
        public string IPTOS { get; set; }
        public int IPTTL { get; set; }

        [UnitProperty("Socket/@", UnitPropertyType.Bool)]
        public bool RemoveOnExit { get; set; }
        public int Mark { get; set; }
        [UnitProperty("Socket/@", UnitPropertyType.Bool)]
        public bool ReusePort { get; set; }
        [UnitProperty("Socket/@")]
        public string FileDescriptorName { get; set; }

        [UnitProperty("Socket/@", UnitPropertyType.StringList)]
        public List<string> Symlinks { get; set; }
        public bool RemoveOnStop { get; set; }

        [UnitProperty("Socket/@")]
        public string Service { get; set; }

        [UnitProperty("Service/@", UnitPropertyType.StringList)]
        public List<string> ExecStartPre { get; set; }

        [UnitProperty("Service/@", UnitPropertyType.StringList)]
        public List<string> ExecStartPost { get; set; }

        [UnitProperty("Service/@", UnitPropertyType.StringList)]
        public List<string> ExecStopPre { get; set; }

        [UnitProperty("Service/@", UnitPropertyType.StringList)]
        public List<string> ExecStopPost { get; set; }
    }
}
