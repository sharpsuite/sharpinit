using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Mono.Unix;
using Mono.Unix.Native;
using NLog;
using SharpInit.Units;

namespace SharpInit.Platform.Unix
{
    public class EpollClient
    {
        public Logger Log = LogManager.GetCurrentClassLogger();
        
        public FileDescriptor ReadFd { get; set; }
        public FileDescriptor WriteFd { get; set; }

        public string Name { get; set; }
        public DateTime Opened { get; set; }

        public bool IsOpen { get; set; }

        public EpollClient(string name)
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

            ReadFd = new FileDescriptor(read, $"{Name}-read", -1);
            WriteFd = new FileDescriptor(write, $"{Name}-write", -1);

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

        public virtual void ClientDataReceived()
        {
        }
    }

    public class UnixEpollManager
    {
        private List<EpollClient> Clients = new List<EpollClient>();
        private ConcurrentDictionary<int, EpollClient> ClientsByReadFd = new ConcurrentDictionary<int, EpollClient>();

        private Logger Log = LogManager.GetCurrentClassLogger();
        
        private FileDescriptor EpollFd;
        public string Name { get; set; }

        public UnixEpollManager(string name)
        {
            Name = name;
            EpollFd = new FileDescriptor(Syscall.epoll_create(int.MaxValue), $"{Name}-epoll", -1);
            new System.Threading.Thread(new System.Threading.ThreadStart(delegate { this.Loop(); })).Start();
        }

        public EpollClient AddClient(EpollClient client)
        {
            lock (Clients)
            {
                Clients.Add(client);
                ClientsByReadFd[client.ReadFd.Number] = client;

                int epoll_add_resp = Syscall.epoll_ctl(EpollFd.Number, EpollOp.EPOLL_CTL_ADD, client.ReadFd.Number,
                    EpollEvents.EPOLLIN);

                if (epoll_add_resp != 0)
                {
                    throw new Exception($"epoll_ctl failed with errno: {Syscall.GetLastError()}");
                }

                return client;
            }
        }

        public void RemoveClient(EpollClient client)
        {
            var ctl_resp = Syscall.epoll_ctl(EpollFd.Number, EpollOp.EPOLL_CTL_DEL, client.ReadFd?.Number ?? -1,
                EpollEvents.EPOLLIN);
            client.Deallocate();
            Clients.Remove(client);
            ClientsByReadFd.TryRemove(new KeyValuePair<int, EpollClient>(client.ReadFd.Number, client));

            if (ctl_resp != 0)
                Log.Error($"epoll_ctl remove failed with errno: {Syscall.GetLastError()}");
        }


        public void Loop()
        {
            int event_size = 128;
            int read_buffer_size = 256;

            EpollEvent[] event_arr = new EpollEvent[event_size];
            var buf = System.Runtime.InteropServices.Marshal.AllocHGlobal(read_buffer_size);

            while (true)
            {
                var wait_ret = Syscall.epoll_wait(EpollFd.Number, event_arr, event_arr.Length, 1000);
                
                Log.Debug($"{wait_ret} events raised from epoll {EpollFd.Number} {Name}");

                for (int i = 0; i < wait_ret; i++)
                {
                    var client_found = ClientsByReadFd.TryGetValue(event_arr[i].fd, out EpollClient client);
                    if (!client_found)
                        client = null;

                    long read = 0;
                    var contents = new List<byte>();

                    if (client == default)
                    {
                        Log.Warn($"Read from unrecognized {Name} fd {event_arr[i].fd}");
                    }

                    if ((event_arr[i].events & (EpollEvents.EPOLLERR | EpollEvents.EPOLLHUP)) > 0)
                    {
                        if (client?.IsOpen == true)
                        {
                            Log.Debug($"{client.Name} closed");
                            RemoveClient(client);
                        }
                        else
                        {
                            Syscall.epoll_ctl(EpollFd.Number, EpollOp.EPOLL_CTL_DEL, event_arr[i].fd,
                                EpollEvents.EPOLLIN);
                        }

                        continue;
                    }

                    if (!event_arr[i].events.HasFlag(EpollEvents.EPOLLIN))
                    {
                        Log.Warn($"received unknown epoll event {event_arr[i].events}");
                        continue;
                    }
                    
                    client?.ClientDataReceived();
                }
            }
        }
    }
}