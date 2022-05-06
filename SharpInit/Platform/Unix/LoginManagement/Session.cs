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

        public async Task<object> GetAsync(string key)
        {
            Log.Debug($"Querying key {key} on session {SessionId}");

            switch (key)
            {
                case "Active":
                    return State == SessionState.Active || State == SessionState.Online;
            }

            return null;
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

        public async Task ReleaseControlAsync()
        {
            foreach (var dev in SessionDevices)
            {
                try
                {
                    dev.Value.FreeDevice();
                }
                catch (Exception e)
                {
                    Log.Error(e, $"Exception thrown while freeing {dev.Key}");
                }
            }
            
            SessionDevices.Clear();
            LoginInterop.RestoreVT(this);
        }

        private SessionDevice OpenDevice(UdevDevice dev, bool active)
        {
            SessionDevice session_device;

            int r = 0;

            if (!SessionDevices.ContainsKey(dev.SysPath))
            {
                session_device = new SessionDevice(dev);
                session_device.Session = this;

                var dev_path = dev.DevPaths.FirstOrDefault();
                
                if (session_device.DeviceType == SessionDeviceType.Evdev)
                {
                    dev_path = $"/dev/input/{dev.SysPath.Split('/').Last()}";
                }
                
                Log.Debug($"opening device {dev.SysPath} under devpath {dev_path}");
                r = Syscall.open(dev_path,
                    OpenFlags.O_RDWR | OpenFlags.O_CLOEXEC | OpenFlags.O_NOCTTY | OpenFlags.O_NONBLOCK);
                
                // open(sd->node, O_RDWR|O_CLOEXEC|O_NOCTTY|O_NONBLOCK);

                if (r < 0)
                {
                    Log.Warn($"open call failed for device {dev.SysPath}: {r} {Syscall.GetLastError()}");
                    return null;
                }

                session_device.DeviceFd = r;
                SessionDevices[dev.SysPath] = session_device;
                return session_device;
            }
            else
            {
                return SessionDevices[dev.SysPath];
            }

            if (session_device.DeviceType == SessionDeviceType.DRM)
            {
                if (active)
                {
                    Log.Debug($"setting master for DRM device {dev.SysPath}");
                    r = TtyUtilities.Ioctl(session_device.DeviceFd, LoginInterop.DRM_IOCTL_SET_MASTER, 0);

                    if (r < 0)
                    {
                        Log.Warn($"set master failed for DRM device {dev.SysPath}: {r} {Syscall.GetLastError()}");
                    }
                }
                else
                {
                    Log.Debug($"dropping master for DRM device {dev.SysPath}");
                    
                    r = TtyUtilities.Ioctl(session_device.DeviceFd, LoginInterop.DRM_IOCTL_DROP_MASTER, 0);
                    if (r < 0)
                    {
                        Log.Warn($"set master failed for DRM device {dev.SysPath}: {r} {Syscall.GetLastError()}");
                    }
                }
            }
            
            Log.Debug($"opened device {dev.SysPath}, returning fd {session_device.DeviceFd}");
            //return (new CloseSafeHandle(new IntPtr(session_device.DeviceFd), false), true);
        }

        public async Task ReleaseDeviceAsync(uint major, uint minor)
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
                return;
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
                Log.Warn($"Asked to release unknown device {dev.SysPath}");
                return;
            }

            session_device = SessionDevices[dev.SysPath];
            session_device.StopDevice();
        }

        public async Task SetTypeAsync(string type)
        {
            if (Enum.TryParse(type, true, out SessionType newType))
            {
                Type = newType;
                Log.Info($"Session {SessionId} has new type {newType}");
            }
            else
            {
                Log.Warn($"Unrecognized session type {type}");
            }
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
                SessionDevices[dev.SysPath] = OpenDevice(dev, true);
            
            session_device = SessionDevices[dev.SysPath];
            session_device.StartDevice();
            
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