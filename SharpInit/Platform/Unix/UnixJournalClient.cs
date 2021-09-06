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

        public void AllocateDescriptors()
        {
            if (ReadFd != null || WriteFd != null)
            {
                throw new Exception("File descriptors already allocated");
            }

            Syscall.pipe(out int read, out int write);

            ReadFd = new FileDescriptor(read, $"journal-{Name}-read", -1);
            WriteFd = new FileDescriptor(write, $"journal-{Name}-write", -1);

            IsOpen = true;
            Opened = DateTime.UtcNow;
        }

        public void Deallocate()
        {
            IsOpen = false;

            if (ReadFd != null) 
                Syscall.close(ReadFd.Number);
            
            if (WriteFd != null)
                Syscall.close(WriteFd.Number);
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