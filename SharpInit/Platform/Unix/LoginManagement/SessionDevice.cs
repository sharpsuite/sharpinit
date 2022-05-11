using Mono.Unix.Native;
using NLog;

namespace SharpInit.Platform.Unix.LoginManagement
{
    public class SessionDevice
    {
        private static Logger Log = LogManager.GetCurrentClassLogger();
        
        public uint DeviceNumber { get; set; }
        public string SysPath { get; set; }
        public int DeviceFd { get; set; }
        public SessionDeviceType DeviceType { get; set; }
        public UdevDevice Device { get; set; }
        
        public Session Session { get; set; }
        public bool Active { get; set; }
        
        public SessionDevice(UdevDevice dev)
        {
            Device = dev;
            SysPath = Device.SysPath;

            if (Device.Subsystem == "drm")
                DeviceType = SessionDeviceType.DRM;
            if (Device.Subsystem == "input")
                DeviceType = SessionDeviceType.Evdev;
        }

        public void StartDevice()
        {
            if (Active)
                return;
            
            int r = 0;
            if (DeviceType == SessionDeviceType.DRM)
            {
                Log.Debug($"setting master for DRM device {SysPath}");
                r = TtyUtilities.Ioctl(DeviceFd, LoginInterop.DRM_IOCTL_SET_MASTER, 0);
                    
                if (r < 0)
                {
                    Log.Warn($"set master failed for DRM device {SysPath}: {r} {Syscall.GetLastError()}");
                }
            }

            Active = true;
        }

        public void StopDevice()
        {
            if (!Active)
                return;

            int r = 0;
            
            if (DeviceType == SessionDeviceType.DRM)
            {
                Log.Debug($"dropping master for DRM device {SysPath}");
                r = TtyUtilities.Ioctl(DeviceFd, LoginInterop.DRM_IOCTL_DROP_MASTER, 0);
                    
                if (r < 0)
                {
                    Log.Warn($"drop master failed for DRM device {SysPath}: {r} {Syscall.GetLastError()}");
                }
            }
            else if (DeviceType == SessionDeviceType.Evdev)
            {
                r = LoginInterop.EviocRevoke(DeviceFd);
                if (r < 0)
                {
                    Log.Warn($"eviocrevoke returned {r} for {SysPath}");
                }
            }
            
            Active = false;
        }

        public void FreeDevice()
        {
            if (DeviceFd > 0)
            {
                StopDevice();
                Syscall.close(DeviceFd);
            }

            Active = false;
        }
    }
}