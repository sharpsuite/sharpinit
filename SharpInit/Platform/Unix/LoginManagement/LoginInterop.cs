using System.Runtime.InteropServices;
using Mono.Unix;
using Mono.Unix.Native;
using Newtonsoft.Json;
using NLog;

namespace SharpInit.Platform.Unix.LoginManagement
{
    public struct vt_mode
    {
        public byte mode;              /* vt mode */
        public byte waitv;             /* if set, hang on writes if not active */
        public short relsig;           /* signal to raise on release req */
        public short acqsig;           /* signal to raise on acquisition */
        public short frsig;            /* unused (set to 0) */
    };

    public static class LoginInterop
    {
        public static Logger Log = LogManager.GetCurrentClassLogger();
        
        [DllImport("libc", EntryPoint = "ioctl", SetLastError = true)]
        internal static extern int Ioctl(int handle, uint request, ref vt_mode mode);
        
        private static readonly uint KDSKBMODE = 0x4B45;
        private static readonly uint K_OFF = 0x04;
        private static readonly uint KDSETMODE = 0x4b3a;
        private static readonly uint KD_GRAPHICS = 0x01;
        private static readonly uint VT_GETMODE = 0x5601;
        private static readonly uint VT_SETMODE = 0x5602;
        internal static readonly uint DRM_IOCTL_SET_MASTER = 25630;
        internal static readonly uint DRM_IOCTL_DROP_MASTER = 25631;
        
        internal static bool PrepareVT(Session session)
        {
            if (session.VTFd == 0)
            {
                session.VTFd = TtyUtilities.OpenTty($"/dev/tty{session.VTNumber}",
                    OpenFlags.O_RDWR | OpenFlags.O_NONBLOCK | OpenFlags.O_NOCTTY | OpenFlags.O_CLOEXEC).FileDescriptor.Number;

                //var chown_result = Syscall.fchown(session.VTFd, (uint)session.UserId);
            }
            
            Log.Debug($"Session {session.SessionId} has VT fd {session.VTFd}");
            
            new Mono.Unix.UnixFileInfo($"/dev/tty{session.VTNumber}").SetOwner(new UnixUserInfo(session.UserId));

            var r = TtyUtilities.Ioctl(session.VTFd, KDSKBMODE, K_OFF);
            if (r != 0) { Log.Error($"ioctl call failed with {r}, errno: {Syscall.GetLastError()}"); return false; }
            
            r = TtyUtilities.Ioctl(session.VTFd, KDSETMODE, KD_GRAPHICS);
            if (r != 0) { Log.Error($"ioctl call failed with {r}, errno: {Syscall.GetLastError()}"); return false; }

            var vt_mode = new vt_mode()
            {
                mode = 1,
                relsig = (int)Signum.SIGUSR1,
                acqsig = (int)Signum.SIGUSR2
            };

            r = Ioctl(session.VTFd, VT_SETMODE, ref vt_mode);
            if (r != 0) { Log.Error($"ioctl call failed with {r}, errno: {Syscall.GetLastError()}"); return false; }

            var vt_mode_tmp = new vt_mode();

            Ioctl(session.VTFd, VT_GETMODE, ref vt_mode_tmp);
            
            Log.Debug($"VT_GETMODE: {JsonConvert.SerializeObject(vt_mode_tmp)}");
            
            return true;
        }
    }
}