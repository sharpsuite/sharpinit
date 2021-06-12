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

        public string SocketUser { get; set; }
        public string SocketGroup { get; set; }

        public int SocketMode { get; set; }
        public int DirectoryMode { get; set; }
        public bool Accept { get; set; }
        public bool Writable { get; set; }

        public bool FlushPending { get; set; }
        public int MaxConnections { get; set; }
        public int MaxConnectionsPerSource { get; set; }
        public bool KeepAlive { get; set; }
        public int KeepAliveTimeSec { get; set; }
        public int KeepAliveProbes { get; set; }
        public bool NoDelay { get; set; }
        public int Priority { get; set; }
        public int DeferAcceptSec { get; set; }
        public int ReceiveBuffer { get; set; }
        public int SendBuffer { get; set; }
        public string IPTOS { get; set; }
        public int IPTTL { get; set; }
        public int Mark { get; set; }
        public bool ReusePort { get; set; }
        public string FileDescriptorName { get; set; }
        public List<string> Symlinks { get; set; }
        public bool RemoveOnStop { get; set; }

        [UnitProperty("Socket/@")]
        public string Service { get; set; }
        public int TimeoutSec { get; set; }

        [UnitProperty("Service/ExecStartPre", UnitPropertyType.StringList)]
        public List<string> ExecStartPre { get; set; }

        [UnitProperty("Service/ExecStartPost", UnitPropertyType.StringList)]
        public List<string> ExecStartPost { get; set; }
    }
}
