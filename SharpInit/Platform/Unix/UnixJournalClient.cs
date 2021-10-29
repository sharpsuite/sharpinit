using Mono.Unix.Native;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using NLog;
using NLog.Targets;
using NLog.Config;
using System.Collections.Concurrent;

namespace SharpInit.Platform.Unix
{
    public class JournalClient : EpollClient
    {

        public JournalClient(string name) : base(name)
        {
        }

        public override void ClientDataReceived()
        {
            int read_buffer_size = 256;
            var buf = System.Runtime.InteropServices.Marshal.AllocHGlobal(read_buffer_size);
            var read = 0l;
            var contents = new List<byte>();
            
            while (true) 
            {
                read = Syscall.read(ReadFd.Number, buf, (ulong)read_buffer_size);

                if (read <= 0) 
                {
                    break;
                }
                        
                var sub_buf = new byte[read];
                System.Runtime.InteropServices.Marshal.Copy(buf, sub_buf, 0, (int)read);
                contents.AddRange(sub_buf);

                if (read < read_buffer_size)
                {
                    break;
                }
            }
                    
            if (read > 0)
            {
                NestedDiagnosticsLogicalContext.Push(Name);
                Log.Info(Encoding.UTF8.GetString(contents.ToArray()));
                NestedDiagnosticsLogicalContext.Pop();
            }
            else
            {
                Log.Warn($"{read} bytes read from journal fd for journal-{Name}");
            }
        }
    }
}