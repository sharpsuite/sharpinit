using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Mono.Unix.Native;
using NLog;
using SharpInit.LoginManager;
using Tmds.DBus;

namespace SharpInit.Platform.Unix.LoginManagement
{
    public class Session : ISession
    {
        private Logger Log = LogManager.GetCurrentClassLogger();
        
        public LoginManager LoginManager { get; set; }
        
        public ObjectPath ObjectPath { get; internal set; }
        public string SessionId { get; set; }
        public int UserId { get; set; }
        public string UserName { get; set; }
        public SessionClass Class { get; set; }
        public SessionType Type { get; set; }
        public SessionState State { get; set; }
        public int VTNumber { get; set; }
        public string TTYPath { get; set; }
        public string Display { get; set; }
        public int LeaderPid { get; set; }
        public string ActiveSeat { get; set; }
        public int ReaderFd { get; set; }
        public string StateFile { get; set; }
        
        internal int VTFd { get; set; }

        public Dictionary<string, SessionDevice> SessionDevices { get; set; } = new();

        public Session(LoginManager manager, string session_id)
        {
            LoginManager = manager;
            SessionId = session_id;
            ObjectPath = new ObjectPath($"/org/freedesktop/login1/Session/{session_id}");
            Log.Debug($"Session object path is {ObjectPath}");
            StateFile = $"/run/systemd/sessions/{session_id}";
        }

        public async Task ActivateAsync()
        {
            Log.Debug($"Asked to activate session {SessionId}");
        }

        public async Task TakeControlAsync(bool force)
        {
            var prepare_result = LoginInterop.PrepareVT(this);
            Log.Debug($"prepare vt result is {prepare_result}");
        }

        public async Task<(CloseSafeHandle, bool)> TakeDeviceAsync(uint major, uint minor)
        {
            uint makedev(int maj, int min)
            {
                long __dev;
                __dev = (((maj & 0x00000fffu)) <<  8);       
                __dev |= (((maj & 0xfffff000u)) << 32);      
                __dev |= (((min & 0x000000ffu)) <<  0);       
                __dev |= (((min & 0xffffff00u)) << 12);
                return (uint)(__dev & 0xffffffff);
            }

            var r = 0;
            var dev = UdevDevice.FromDevNum((int)major, (int)minor, false);
            if (dev != null)
            {
                Log.Debug($"Opened device {major}:{minor} with syspath {dev.SysPath}");
            }
            else
            {
                Log.Warn($"Could not find udev device for {major}:{minor}");
                return (new CloseSafeHandle(new IntPtr(-1), false), false);
            }

            if (UdevEnumerator.Devices.ContainsKey(dev.SysPath))
            {
                Log.Debug(
                    $"Replacing device with already-discovered UdevDevice: {dev.SysPath}, subsystem: {dev.Subsystem}");
                dev = UdevEnumerator.Devices[dev.SysPath];
            }

            SessionDevice session_device;

            if (!SessionDevices.ContainsKey(dev.SysPath))
            {
                Log.Debug($"1");
                session_device = new SessionDevice(dev);
                session_device.Session = this;


                //Log.Debug($"5");
                var dev_path = dev.DevPaths.FirstOrDefault();
                
                if (session_device.DeviceType == SessionDeviceType.Evdev)
                {
                    Log.Debug($"3");
                    dev_path = $"/dev/input/{dev.SysPath.Split('/').Last()}";
                }
                
                Log.Debug($"opening device {dev.SysPath} under devpath {dev_path}");
                r = Syscall.open(dev_path,
                    OpenFlags.O_RDWR | OpenFlags.O_CLOEXEC | OpenFlags.O_NOCTTY | OpenFlags.O_NONBLOCK);
                
                Log.Debug($"2 {session_device.DeviceType}");

                Log.Debug($"4");
                if (session_device.DeviceType == SessionDeviceType.Other)
                {
                    return (new CloseSafeHandle(new IntPtr(r), false), true);
                }
                // open(sd->node, O_RDWR|O_CLOEXEC|O_NOCTTY|O_NONBLOCK);

                if (r < 0)
                {
                    Log.Warn($"open call failed for device {dev.SysPath}: {r} {Syscall.GetLastError()}");
                    return (new CloseSafeHandle(new IntPtr(-1), false), false);
                }

                session_device.DeviceFd = r;
            }
            else
            {
                session_device = SessionDevices[dev.SysPath];
            }

            if (session_device.DeviceType == SessionDeviceType.DRM)
            {
                Log.Debug($"setting master for DRM device {dev.SysPath}");
                r = TtyUtilities.Ioctl(session_device.DeviceFd, LoginInterop.DRM_IOCTL_SET_MASTER, 0);
                    
                if (r < 0)
                {
                    Log.Warn($"set master failed for DRM device {dev.SysPath}: {r} {Syscall.GetLastError()}");
                    return (new CloseSafeHandle(new IntPtr(-1), false), false);
                }
            }
            
            Log.Debug($"opened device {dev.SysPath}, returning fd {session_device.DeviceFd}");
            return (new CloseSafeHandle(new IntPtr(session_device.DeviceFd), false), true);
        }
    }

    public enum SessionDeviceType
    {
        Other,
        DRM,
        Evdev
    }
}