using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Linq;

using SharpInit.Tasks;

using Mono.Unix.Native;

using NLog;

namespace SharpInit.Platform.Unix
{
    /// <summary>
    /// Unix platform initialization code. Sets up signals.
    /// </summary>
    [SupportedOn("unix")]
    public class UnixPlatformInitialization : GenericPlatformInitialization
    {
        public static bool IsSystemManager = false;
        public static bool UnderSystemd = false;
        public static bool IsPrivileged = false;

        Logger Log = LogManager.GetCurrentClassLogger();

        public override void Initialize()
        {
            base.Initialize();
            SignalHandler.Initialize();
            
            IsSystemManager = Syscall.getpid() == 1;
            
            Log.Debug($"SharpInit is {(IsSystemManager ? "the" : "not the")} system manager.");

            IsPrivileged = Syscall.getuid() == 0;
            UnderSystemd = Directory.Exists("/var/run/systemd");

            var sid = Syscall.setsid();
            Log.Debug($"New session id is {sid}");

            if (IsSystemManager)
            {
                var devnull = Syscall.open("/dev/null", OpenFlags.O_RDWR | OpenFlags.O_CLOEXEC);

                if (devnull < 0)
                {
                    throw new Exception($"Could not open /dev/null: open() returned {devnull}, errno is {Syscall.GetLastError()}");
                }

                Log.Debug($"dup2: {Syscall.dup2(devnull, 0)}");
                Log.Debug($"dup2: {Syscall.dup2(devnull, 1)}");
                Log.Debug($"dup2: {Syscall.dup2(devnull, 2)}");
                
                Log.Debug($"Releasing tty: {TtyUtilities.ReleaseTerminal()}");
            }
        }

        public override void LateInitialize()
        {
            base.LateInitialize();
            UnixMountWatcher.ServiceManager = Program.ServiceManager;
            UnixMountWatcher.MountChanged += UnixMountWatcher.SynchronizeMountUnit;
            UnixMountWatcher.ParseFstab();
            UnixMountWatcher.StartWatching();
        }
    }
}
