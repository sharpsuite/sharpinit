using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpInit.Units
{
    public class SocketUnitDescriptor : ExecUnitDescriptor
    {
        public List<string> ListenStream { get; set; }
        public List<string> ListenDatagram { get; set; }
        public List<string> ListenSequentialPacket { get; set; }

        public List<string> ListenFIFO { get; set; }
        public List<string> ListenSpecial { get; set; }
        public List<string> ListenNetlink { get; set; }
        public List<string> ListenMessageQueue { get; set; }
        public List<string> ListenUSBFunction { get; set; }

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
        public string Service { get; set; }
        public int TimeoutSec { get; set; }

        [UnitProperty("Service/ExecStartPre", UnitPropertyType.StringList)]
        public List<string> ExecStartPre { get; set; }

        [UnitProperty("Service/ExecStartPost", UnitPropertyType.StringList)]
        public List<string> ExecStartPost { get; set; }
    }
}
