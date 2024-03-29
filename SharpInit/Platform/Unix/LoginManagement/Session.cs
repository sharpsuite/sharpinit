using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Mono.Unix.Native;
using NLog;
using SharpInit.LoginManager;
using Tmds.DBus;

namespace SharpInit.Platform.Unix.LoginManagement
{
    public class Session : ISession
    {
        public event Action OnLocked;
        public event Action OnUnlocked;
        private Logger Log = LogManager.GetCurrentClassLogger();
        
        public LoginManager LoginManager { get; set; }
        
        public ObjectPath ObjectPath { get; internal set; }

        public string SessionId { get; set; } = "";
        public int UserId { get; set; }
        public string UserName { get; set; } = "";
        public SessionClass Class { get; set; }
        public SessionType Type { get; set; }
        public SessionState State { get; set; }
        public int VTNumber { get; set; }
        public string TTYPath { get; set; } = "";
        public string Display { get; set; } = "";
        public int LeaderPid { get; set; }
        public string ActiveSeat { get; set; } = "";
        public int ReaderFd { get; set; }
        public string StateFile { get; set; } = "";
        public string PAMService { get; set; } = "";
        
        internal int VTFd { get; set; }

        public Dictionary<string, SessionDevice> SessionDevices { get; set; } = new();

        private DateTime _sessionCreated;

        internal Session()
        {
            ObjectPath = new ObjectPath("/org/freedesktop/login1/session/invalid");
        }

        public Session(LoginManager manager, string session_id)
        {
            LoginManager = manager;
            SessionId = session_id;
            ObjectPath = new ObjectPath($"/org/freedesktop/login1/session/_3{session_id}");
            Log.Debug($"Session object path is {ObjectPath}");
            StateFile = $"/run/systemd/sessions/{session_id}";
            _sessionCreated = DateTime.UtcNow;
        }
        
        public Task<IDisposable> WatchLockAsync(Action handler)
        {
            return SignalWatcher.AddAsync(this, nameof(OnLocked), handler);
        }

        public Task<IDisposable> WatchUnlockAsync(Action handler)
        {
            return SignalWatcher.AddAsync(this, nameof(OnUnlocked), handler);
        }

        public async Task UnlockSession()
        {
            OnUnlocked?.Invoke();
        }

        public async Task<IDictionary<string, object>> GetAllAsync()
        {
            string scope = "";
            
            if (LeaderPid > 0)
            {
                try
                {
                    var cgroup = File.ReadAllText($"/proc/{LeaderPid}/cgroup").Trim();
                    var parts = cgroup.Split('/');
                    
                    foreach (var part in parts.Reverse())
                        if (part.EndsWith(".scope"))
                        {
                            scope = part;
                            break;
                        }
                }
                catch (Exception e)
                {
                }
            }
            
            return new Dictionary<string, object>()
            {
                {"Id", SessionId},
                {"User", (UserId, (LoginManager.Users.ContainsKey(UserId) ? LoginManager.Users[UserId]?.ObjectPath : new ObjectPath("/")) ?? new ObjectPath("/"))},
                {"Name", UserName},
                {"VTNr", VTNumber},
                {"Seat", (ActiveSeat, LoginManager.Seats.ContainsKey(ActiveSeat) ? LoginManager.Seats[ActiveSeat].ObjectPath : new ObjectPath("/"))},
                {"TTY", TTYPath},
                {"Display", Display},
                {"Remote", false},
                {"RemoteHost", ""},
                {"RemoteUser", ""},
                {"Service", PAMService},
                {"Scope", scope},
                {"Leader", LeaderPid},
                {"Type", Type.ToString().ToLowerInvariant()},
                {"Class", Class == SessionClass.LockScreen ? "lock-screen" : Class.ToString().ToLowerInvariant()},
                {"Active", true},
                {"IdleHint", false},
                {"State", "active"},
                {"Timestamp", (long)((_sessionCreated - DateTime.UnixEpoch).TotalMilliseconds * 1000)},
                {"TimestampMonotonic", (long)((_sessionCreated - DateTime.UnixEpoch).TotalMilliseconds * 1000)},
            };
        }
        
        public async Task<object> GetAsync(string key)
        {
            var dict = await GetAllAsync();
            if (!dict.ContainsKey(key))
                return null;
            return dict[key];
        }

        public async Task ActivateAsync()
        {
            Log.Debug($"Asked to activate session {SessionId}");
            State = SessionState.Active;
            
            if (LoginManager.Seats.ContainsKey(ActiveSeat))
            {
                var prevActive = LoginManager.Seats[ActiveSeat].ActiveSession;

                if (prevActive != null)
                {
                    Log.Info($"Previous active session for seat {ActiveSeat} was {prevActive}");
                    if (prevActive != SessionId)
                    {
                        if (LoginManager.Sessions.ContainsKey(prevActive))
                            await LoginManager.Sessions[prevActive].ReleaseControlAsync();
                    }
                }

                LoginManager.Seats[ActiveSeat].ActiveSession = SessionId;
            }

            Save();
        }

        public void Save()
        {
            var session_file_contents = new StringBuilder();
            session_file_contents.AppendLine($"ACTIVE=1");
            session_file_contents.AppendLine($"IS_DISPLAY=1");
            session_file_contents.AppendLine($"STATE={(State.HasFlag(SessionState.Active) ? "active" : State.HasFlag(SessionState.Online) ? "online" : "closing")}");
            session_file_contents.AppendLine($"TTY={TTYPath}");
            session_file_contents.AppendLine($"LEADER={LeaderPid}");
            session_file_contents.AppendLine($"TYPE={Type.ToString().ToLowerInvariant()}");
            session_file_contents.AppendLine($"SEAT={ActiveSeat}");
            session_file_contents.AppendLine($"UID={UserId}");
            session_file_contents.AppendLine($"USER={UserName}");
            session_file_contents.AppendLine($"VTNR={VTNumber}");
            session_file_contents.AppendLine($"CLASS={Class.ToString().ToLowerInvariant()}");
            
            Directory.CreateDirectory(Path.GetDirectoryName(StateFile));
            File.WriteAllText(StateFile, session_file_contents.ToString());
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

        public async Task SetBrightnessAsync(string subsystem, string name, uint brightness)
        {
            Log.Warn($"Asked to set brightness of session {SessionId} device {subsystem}/{name} to {brightness}, not yet implemented");
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