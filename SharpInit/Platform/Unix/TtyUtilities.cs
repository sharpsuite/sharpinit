using Mono.Unix.Native;

using System;
using System.IO;
using System.Runtime.InteropServices;

using NLog;

namespace SharpInit.Platform.Unix
{
    public class Tty : IDisposable
    {
        public FileDescriptor FileDescriptor { get; set; }

        internal Tty(int fd, string name = "tty")
        {
            FileDescriptor = new FileDescriptor(fd, name, -1);
        }

        void IDisposable.Dispose()
        {
            if (FileDescriptor?.Number > 0)
            {
                Syscall.close(FileDescriptor.Number);
                FileDescriptor = null;
            }
        }
    }

    public static class TtyUtilities
    {
        static Logger Log = LogManager.GetCurrentClassLogger();

        [DllImport("libc", EntryPoint = "ioctl", SetLastError = true)]
        internal static extern int Ioctl(int handle, uint request);
        [DllImport("libc", EntryPoint = "ioctl", SetLastError = true)]
        internal static extern int Ioctl(int handle, uint request, uint sub);
        [DllImport("libc", EntryPoint = "tcgetattr", SetLastError = true)]
        internal static extern int TcGetAttr(int handle, ref Termios t);
        [DllImport("libc", EntryPoint = "tcsetattr", SetLastError = true)]
        internal static extern int TcSetAttr(int handle, int opt_actions, ref Termios t);
        [DllImport("libc", EntryPoint = "tcflush", SetLastError = true)]
        internal static extern int TcFlush(int handle, int queue_selector);
        
        static readonly uint KDSETMODE = 0x4b3a;
        static readonly uint KD_TEXT = 0;

        static readonly uint TIOCEXCL = 0x540c;
        static readonly uint TIOCNXCL = 0x540d;
        static readonly uint TIOCVHANGUP = 0x5437;
        static readonly uint TIOCNOTTY = 0x5422;
        static readonly uint VT_DISALLOCATE = 0x5608;
        static readonly uint VT_ACTIVATE = 0x5606;
        public static readonly uint VT_RELDISP = 0x5605;

        public static void Chvt(int next_vt)
        {
            using (var tty = OpenTty("/dev/tty0"))
            {
                var r = Ioctl(tty.FileDescriptor.Number, VT_ACTIVATE, (uint) next_vt);
                
                if (r != 0)
                    Log.Warn($"VT_ACTIVATE returned {r}");
            }
        }

        public static Tty OpenTty(string name, OpenFlags mode = (OpenFlags.O_RDWR | OpenFlags.O_NOCTTY | OpenFlags.O_CLOEXEC | OpenFlags.O_NONBLOCK))
        {
            var fd = -1;
            int tried = 0;

            while (true)
            {
                fd = Syscall.open(name, mode);

                if (fd >= 0)
                    break;
                
                if (Syscall.GetLastError() != Errno.EIO || tried++ >= 20)
                {
                    throw new Exception($"Cannot open tty {name}: {Syscall.GetLastError()}, open() returns {fd}");
                }
                
                System.Threading.Thread.Sleep(0);
            }

            return new Tty(fd, Path.GetFileName(name));
        }

        public static int VhangupTty(string name) 
        {
            using (var tty = OpenTty(name)) 
            { 
                return VhangupTty(tty); 
            }
        }

        public static int VhangupTty(Tty tty)
        {
            if (tty == null || tty.FileDescriptor == null)
                throw new Exception("No tty given");

            var fd = tty.FileDescriptor.Number;

            if (fd < 0)
            {
                Log.Warn($"Invalid fd given: {fd}");
                return -1;
            }
            
            if (Ioctl(fd, TIOCVHANGUP) < 0)
            {
                Log.Warn($"vhangup failed with errno {Syscall.GetLastError()}");
                return -(int)Syscall.GetLastError();
            }
            
            return 0;
        }

        public static int ResetTty(string name)
        {
            using (var tty = OpenTty(name))
            {
                return ResetTty(tty);
            }
        }

        public static int ResetTty(Tty tty, bool switch_to_text = false)
        {
            if (tty == null || tty.FileDescriptor == null)
                throw new Exception("No tty given");

            var fd = tty.FileDescriptor.Number;

            int ret = 0;

            if (!Syscall.isatty(fd))
                throw new Exception($"ResetTty: not a terminal");
            
            try 
            {
                if (Ioctl(fd, TIOCNXCL) < 0)
                    throw new Exception("TIOCNXCL ioctl failed");
                
                if (switch_to_text)
                    if (Ioctl(fd, KDSETMODE, KD_TEXT) < 0)
                        throw new Exception("KDSETMODE ioctl failed");
                
                Termios termios = new Termios();
                termios.c_cc = new char[19];

                if (TcGetAttr(fd, ref termios) < 0)
                    throw new Exception("Failed to get terminal parameters");
                
                termios.c_iflag &= ~(c_iflag.IGNBRK | c_iflag.BRKINT | c_iflag.ISTRIP | c_iflag.INLCR | c_iflag.IGNCR | c_iflag.IUCLC);
                termios.c_iflag |= c_iflag.ICRNL | c_iflag.IMAXBEL | c_iflag.IUTF8;
                termios.c_oflag |= c_oflag.ONLCR;
                termios.c_cflag |= 128; // CREAD
                termios.c_lflag = c_lflag.ISIG | c_lflag.ICANON | c_lflag.IEXTEN | c_lflag.ECHO | c_lflag.ECHOE | c_lflag.ECHOK | c_lflag.ECHOCTL | c_lflag.ECHOPRT | c_lflag.ECHOKE;

                termios.c_cc[(int)c_cc.VINTR]    = (char)   3;  /* ^C */
                termios.c_cc[(int)c_cc.VQUIT]    = (char)  28;  /* ^\ */
                termios.c_cc[(int)c_cc.VERASE]   = (char) 127;
                termios.c_cc[(int)c_cc.VKILL]    = (char)  21;  /* ^X */
                termios.c_cc[(int)c_cc.VEOF]     = (char)   4;  /* ^D */
                termios.c_cc[(int)c_cc.VSTART]   = (char)  17;  /* ^Q */
                termios.c_cc[(int)c_cc.VSTOP]    = (char)  19;  /* ^S */
                termios.c_cc[(int)c_cc.VSUSP]    = (char)  26;  /* ^Z */
                termios.c_cc[(int)c_cc.VLNEXT]   = (char)  22;  /* ^V */
                termios.c_cc[(int)c_cc.VWERASE]  = (char)  23;  /* ^W */
                termios.c_cc[(int)c_cc.VREPRINT] = (char)  18;  /* ^R */
                termios.c_cc[(int)c_cc.VEOL]     = (char)   0;
                termios.c_cc[(int)c_cc.VEOL2]    = (char)   0;

                termios.c_cc[(int)c_cc.VTIME]  = (char) 0;
                termios.c_cc[(int)c_cc.VMIN]   = (char) 1;

                if (TcSetAttr(fd, (int)tc.TCSANOW, ref termios) < 0)
                    throw new Exception("Failed to set terminal parameters");
            }
            catch
            {
                throw;
            }
            finally
            {
                TcFlush(fd, (int)tc.TCIOFLUSH);
            }

            return ret;
        }

        public static int DisallocateTty(string name)
        {
            using (var tty = OpenTty("/dev/tty0"))
            {
                tty.FileDescriptor.Name = name;
                return DisallocateTty(tty);
            }
        }

        public static int DisallocateTty(Tty tty)
        {
            if (tty == null || tty.FileDescriptor == null)
                throw new Exception("No tty given");

            var fd = tty.FileDescriptor.Number;
            var tty_num = GetTtyNumber(tty.FileDescriptor.Name);
            
            Log.Debug($"Detected tty number for {tty.FileDescriptor.Name} as {tty_num}");

            if (tty_num < 0)
                return -1;

            if (Ioctl(fd, VT_DISALLOCATE, (uint)tty_num) >= 0)
                return 0;
            
            Log.Warn($"disallocate tty failed with errno {Syscall.GetLastError()}");
            return -(int)Syscall.GetLastError();
        }

        public static int GetTtyNumber(string path)
        {
            int index = path.IndexOfAny("0123456789".ToCharArray());
            return int.TryParse(path.Substring(index), out int num) ? num : -1;
        }

        public static int ReleaseTerminal()
        {
            using (var tty = OpenTty("/dev/tty"))
            {
                if (tty == null || tty.FileDescriptor == null || tty.FileDescriptor.Number < 0)
                    return -(int)Syscall.GetLastError();

                if (Ioctl(tty.FileDescriptor.Number, TIOCNOTTY) < 0)
                    return -(int)Syscall.GetLastError();
                
                return 0;
            }
        }
    }


    public struct Termios
    {
        public c_iflag c_iflag;
        public c_oflag c_oflag;
        public uint c_cflag;
        public c_lflag c_lflag;
        public char c_line;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 19)]
        public char[] c_cc;
    }

    public enum c_cc
    {
        VINTR = 0,
        VQUIT = 1,
        VERASE = 2,
        VKILL = 3,
        VEOF = 4,
        VTIME = 5,
        VMIN = 6,
        VSWTC = 7,
        VSTART = 8,
        VSTOP = 9,
        VSUSP = 10,
        VEOL = 11,
        VREPRINT = 12,
        VDISCARD = 13,
        VWERASE = 14,
        VLNEXT = 15,
        VEOL2 = 16
    }

    public enum c_iflag
    {
        IGNBRK = 1,
        BRKINT = 2,
        IGNPAR = 4,
        PARMRK = 8,
        INPCK = 16,
        ISTRIP = 32,
        INLCR = 64,
        IGNCR = 128,
        ICRNL = 256,
        IUCLC = 512,
        IXON = 1024,
        IXANY = 2048,
        IXOFF = 4096,
        IMAXBEL = 8192,
        IUTF8 = 16384

    }

    public enum c_oflag
    {
        OPOST = 1,
        OLCUC = 2,
        ONLCR = 4,
        OCRNL = 8,
        ONOCR = 16,
        ONLRET = 32,
        OFILL = 64,
        OFDEL = 128,
        NLDLY = 256,
        NL0 = 0,
        NL1 = 256,
        CRDLY = 1536,
        CR0 = 0,
        CR1 = 512,
        CR2 = 1024,
        CR3 = 1536,
        TABDLY = 6144,
        TAB0 = 0,
        TAB1 = 2048,
        TAB2 = 4096,
        TAB3 = 6144,
        XTABS = 6144,
        BSDLY = 8192,
        BS0 = 0,
        BS1 = 8192,
        VTDLY = 16384,
        VT0 = 0,
        VT1 = 16384,
        FFDLY = 32768,
        FF0 = 0,
        FF1 = 32768,

    }

    public enum c_lflag
    {
        ISIG = 1,
        ICANON = 2,
        XCASE = 4,
        ECHO = 8,
        ECHOE = 16,
        ECHOK = 32,
        ECHONL = 64,
        NOFLSH = 128,
        TOSTOP = 256,
        ECHOCTL = 512,
        ECHOPRT = 1024,
        ECHOKE = 2048,
        FLUSHO = 4096,
        PENDIN = 16384,
        IEXTEN = 32768,
        EXTPROC = 65536,
    }

    public enum tc
    {
        TCOOFF = 0,
        TCOON = 1,
        TCIOFF = 2,
        TCION = 3,
        TCIFLUSH = 0,
        TCOFLUSH = 1,
        TCIOFLUSH = 2,
        TCSANOW = 0,
        TCSADRAIN = 1,
        TCSAFLUSH = 2,

    }
}