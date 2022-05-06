using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Mono.Unix.Native;
using SharpInit.Units;

namespace SharpInit.Platform.Unix
{
    public struct ucred
    {
        public int pid;
        public uint uid;
        public uint gid;
    }

    public class NotifyMessage
    {
        public ucred Credentials { get; set; }
        public string Contents { get; set; }
        public DateTime Received { get; set; }
        public FileDescriptor[] FileDescriptors { get; set; }
        
        public NotifyMessage() {}
        public override string ToString() => Contents;
    }
    
    public class NotifyClient : EpollClient
    {
        public ServiceUnit Unit { get; set; }
        public ConcurrentQueue<NotifyMessage> MessageQueue { get; set; } = new();

        public CancellationTokenSource DataRead { get; set; } = new();

        public NotifyClient(ServiceUnit unit, int fd) : base($"notify-{unit.UnitName}")
        {
            ReadFd = new FileDescriptor(fd, Name, -1);
            Unit = unit;
        }
        public async Task<bool> WaitOneAsync(TimeSpan timeout)
        {
            if (!MessageQueue.IsEmpty)
                return true;
            
            var time = Program.ElapsedSinceStartup();
            while (true)
            {
                var token = DataRead.Token;
                await Task.Delay(25, token).ContinueWith(t => {});

                if (!MessageQueue.IsEmpty)
                    return true;
                
                if ((Program.ElapsedSinceStartup() - time) > timeout)
                    return false;
            }
        }

        public NotifyMessage DequeueMessage()
        {
            if (MessageQueue.TryDequeue(out NotifyMessage ret))
                return ret;
            return null;
        }

        private void EnqueueMessage(string contents, ucred credentials, List<FileDescriptor> fds)
        {
            var message = new NotifyMessage()
            {
                Contents = contents,
                Credentials = credentials,
                Received = DateTime.UtcNow,
                FileDescriptors = fds.ToArray()
            };
            
            MessageQueue.Enqueue(message);
            Unit.HandleNotifyMessage(message);
        }
        
        public override unsafe void ClientDataReceived()
        {
            int read_buffer_size = 1024;
            var buf = new byte[read_buffer_size];
            long read = 0;

            var credentials = new ucred();
            var fds = new List<FileDescriptor>();

            var cmsg = new byte[1024];
            var msghdr = new Msghdr {
                msg_control = cmsg,
                msg_controllen = cmsg.Length,
            };

            fixed (byte* ptr_buf = buf)
            {
                var iovecs = new Iovec[] {
                    new()
                    {
                        iov_base = (IntPtr) ptr_buf,
                        iov_len = (ulong) buf.Length,
                    },
                };
                msghdr.msg_iov = iovecs;
                msghdr.msg_iovlen = 1;
                read = (int) Syscall.recvmsg(ReadFd.Number, msghdr,
                    MessageFlags.MSG_CMSG_CLOEXEC | MessageFlags.MSG_TRUNC | MessageFlags.MSG_DONTWAIT);

                if (read < 0)
                {
                    var errno = Syscall.GetLastError();
                    Log.Warn($"Read {read} bytes with errno {errno}");
                    return;
                }
            }
            
            for (var offset = Syscall.CMSG_FIRSTHDR (msghdr); offset != -1; offset = Syscall.CMSG_NXTHDR (msghdr, offset)) {
                var recvHdr = Cmsghdr.ReadFromBuffer (msghdr, offset);
                var recvDataOffset = (int)Syscall.CMSG_DATA (msghdr, offset);
                var bytes = recvHdr.cmsg_len - (recvDataOffset - offset);
                Log.Debug($"Received cmsg type {recvHdr.cmsg_type}, bytes: {BitConverter.ToString(msghdr.msg_control, recvDataOffset, (int)bytes).Replace("-", "").ToLower()}");

                if (recvHdr.cmsg_type == UnixSocketControlMessage.SCM_RIGHTS)
                {
                    var fdCount = bytes / sizeof (int);
                    fixed (byte* ptr = msghdr.msg_control)
                        for (int i = 0; i < fdCount; i++)
                            fds.Add (new FileDescriptor(((int*) (ptr + recvDataOffset))[i], "", -1));
                }
                else if (recvHdr.cmsg_type == UnixSocketControlMessage.SCM_CREDENTIALS)
                {
                    credentials.pid = BitConverter.ToInt32(msghdr.msg_control, recvDataOffset + 0);
                    credentials.uid = BitConverter.ToUInt32(msghdr.msg_control, recvDataOffset + 4);
                    credentials.gid = BitConverter.ToUInt32(msghdr.msg_control, recvDataOffset + 8);

                    foreach (var fd in fds)
                        fd.ProcessId = credentials.pid;

                    // TODO: Verify that caller is authorized
                }
            }

            if (read > 0)
            {
                var old_cts = DataRead;
                DataRead = new CancellationTokenSource();
                old_cts.Cancel();

                var currentString = new StringBuilder();
                for (int i = 0; i < read; i++)
                {
                    char c = (char)buf[i];

                    if (c == '\n')
                    {
                        EnqueueMessage(currentString.ToString(), credentials, fds);
                        currentString.Clear();
                    }
                    else
                    {
                        currentString.Append(c);
                    }
                }

                if (currentString.Length > 0)
                {
                    EnqueueMessage(currentString.ToString(), credentials, fds);
                }
            }
        }
    }
}