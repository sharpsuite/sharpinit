using Mono.Unix.Native;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using NLog;
using NLog.Targets;
using NLog.Config;

namespace SharpInit.Platform.Unix
{
    public class UnixJournalClient
    {
        public FileDescriptor ReadFd { get; set; }
        public FileDescriptor WriteFd { get; set; }

        public string Name { get; set; }
        public DateTime Opened { get; set; }

        public bool IsOpen { get; set; }

        public UnixJournalClient(string name)
        {
            Name = name;
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
    }

    public class UnixJournalManager
    {
        private List<UnixJournalClient> Clients = new List<UnixJournalClient>();

        private Logger Log = LogManager.GetCurrentClassLogger();

        private Journal Journal { get; set; }

        public UnixJournalManager(Journal journal)
        {
            Journal = journal;
            EpollFd = new FileDescriptor(Syscall.epoll_create(int.MaxValue), "journal-epoll", -1);
            new System.Threading.Thread(new System.Threading.ThreadStart(delegate 
            {
                this.JournalLoop();
            })).Start();
        }

        public UnixJournalClient CreateClient(string name)
        {
            var client = new UnixJournalClient(name);
            client.AllocateDescriptors();
            Clients.Add(client);

            int epoll_add_resp = Syscall.epoll_ctl(EpollFd.Number, EpollOp.EPOLL_CTL_ADD, client.ReadFd.Number, EpollEvents.EPOLLIN);

            if (epoll_add_resp != 0)
            {
                throw new Exception($"epoll_ctl failed with errno: {Syscall.GetLastError()}");
            }

            return client;
        }

        private void RemoveClient(UnixJournalClient client)
        {
            var ctl_resp = Syscall.epoll_ctl(EpollFd.Number, EpollOp.EPOLL_CTL_DEL, client.ReadFd?.Number ?? -1, EpollEvents.EPOLLIN);
            client.Deallocate();
            Clients.Remove(client);

            if (ctl_resp != 0)
                throw new Exception($"epoll_ctl remove failed with errno: {Syscall.GetLastError()}");
        }

        private FileDescriptor EpollFd;

        public void JournalLoop()
        {
            int event_size = 128;
            int read_buffer_size = 256;

            EpollEvent[] event_arr = new EpollEvent[event_size];
            var buf = System.Runtime.InteropServices.Marshal.AllocHGlobal(read_buffer_size);
            while (true)
            {
                var wait_ret = Syscall.epoll_wait(EpollFd.Number, event_arr, event_arr.Length, 1);

                for (int i = 0; i < wait_ret; i++) 
                {
                    var client = Clients.FirstOrDefault(client => client.ReadFd.Number == event_arr[i].fd);
                    long read = 0;
                    var contents = new List<byte>();

                    if (client == default)
                    {
                        Log.Warn($"Read from unrecognized journal fd {event_arr[i].fd}");
                    }

                    if ((event_arr[i].events & (EpollEvents.EPOLLERR | EpollEvents.EPOLLHUP)) > 0)
                    {
                        if (client?.IsOpen == true)
                        {
                            Log.Debug($"journal-{client.Name} closed");
                            RemoveClient(client);
                        }
                        else
                        {
                            Syscall.epoll_ctl(EpollFd.Number, EpollOp.EPOLL_CTL_DEL, event_arr[i].fd, EpollEvents.EPOLLIN);
                        }
                        continue;
                    }

                    if (!event_arr[i].events.HasFlag(EpollEvents.EPOLLIN))
                    {
                        Log.Warn($"received unknown epoll event {event_arr[i].events}");
                        continue;
                    }

                    while (true) 
                    {
                        read = Syscall.read(event_arr[i].fd, buf, (ulong)read_buffer_size);

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
                        NLog.NestedDiagnosticsLogicalContext.Push(client.Name ?? $"unknown-{event_arr[i].fd}");
                        //Journal.RaiseJournalData(client.Name, contents.ToArray());
                        Log.Info(Encoding.UTF8.GetString(contents.ToArray()));
                        NLog.NestedDiagnosticsLogicalContext.Pop();
                    }
                    else
                    {
                        Log.Warn($"{read} bytes read from journal fd for journal-{client?.Name}");
                    }
                }
            }
        }
    }
}