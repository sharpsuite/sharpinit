using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Mono.Unix;
using Mono.Unix.Native;
using Newtonsoft.Json;
using NLog;
using SharpInit.LoginManager;
using SharpInit.Units;
using Tmds.DBus;
using Tmds.DBus.Protocol;

namespace SharpInit.Platform.Unix.LoginManagement
{
    [StructLayout(LayoutKind.Sequential)]
    public struct PropertyPair
    {
        public string Key;
        public object Value;
    }
    
    // uusssssussbssa(sv)
    [StructLayout(LayoutKind.Sequential)]
    public struct SessionRequest
    {
        public uint uid;
        public uint pid;
        public string service;
        public string type;
        public string @class;
        public string desktop;
        public string seat_id;
        public uint vtnr;
        public string tty;
        public string display;
        public bool remote;
        public string remote_user;
        public string remote_host;
        public PropertyPair[] properties; // a(sv)
    }
    
    [StructLayout(LayoutKind.Sequential)]
    public struct SessionData
    {
        public string session_id;
        public ObjectPath object_path;
        public string runtime_path;
        public CloseSafeHandle fifo_fd;
        public uint uid;
        public string seat_id;
        public uint vtnr;
        public bool existing;
    }

    [DBusInterface("org.freedesktop.login1.Manager")]
    public interface ILoginDaemon : IDBusObject
    {
        Task<IDictionary<string, object>> GetAllAsync();
        Task<object> GetAsync(string key);
        Task<ObjectPath> GetSeatAsync(string seat_id);
        Task<ObjectPath> GetSessionAsync(string session_id, Tmds.DBus.Protocol.Message message);
        
        Task<(string sessionId, ObjectPath objectPath, string runtimePath, CloseSafeHandle fifoFd, uint uid, string seatId, uint vtnr, bool existing)> CreateSessionAsync(uint Uid, uint Pid
            , string Service, string Type, string Class, string Desktop, string SeatId, uint Vtnr, string 
                Tty, string Display, bool Remote, string RemoteUser, string RemoteHost, (string, object)[] Properties, Message message);
        public Task ReleaseSessionAsync(string session_id);
        Task<IEnumerable<(string, ObjectPath)>> ListSeatsAsync();
        Task<IEnumerable<(string, uint, string, string, ObjectPath)>> ListSessionsAsync();
        Task<IEnumerable<(uint, string, ObjectPath)>> ListUsersAsync();
        Task<ObjectPath> GetSessionByPIDAsync(uint pid);
        Task<CloseSafeHandle> InhibitAsync(string what, string who, string why, string mode);
        
        //CanPowerOff(), CanReboot(), CanHalt(), CanSuspend(), CanHibernate(), CanHybridSleep(),
        //CanSuspendThenHibernate(), CanRebootParameter(), CanRebootToFirmwareSetup(), CanRebootToBootLoaderMenu(),
        //and CanRebootToBootLoaderEntry() test whether the system supports the respective operation and whether the
        //calling user is allowed to execute it. Returns one of "na", "yes", "no", and "challenge". If "na" is returned,
        //the operation is not available because hardware, kernel, or drivers do not support it. If "yes" is returned, the operation is supported and the user may execute the operation without further authentication. If "no" is returned, the operation is available but the user is not allowed to execute the operation. If "challenge" is returned, the operation is available but only after authorization.
        Task<string> CanPowerOffAsync();
        Task<string> CanRebootAsync();
        Task<string> CanHaltAsync();
        Task<string> CanSuspendAsync();
        Task<string> CanHibernateAsync();
        Task<string> CanHybridSleepAsync();
        Task<string> CanSuspendThenHibernateAsync();
        Task<string> CanRebootParameterAsync();
        Task<string> CanRebootToFirmwareSetupAsync();
        Task<string> CanRebootToBootLoaderMenuAsync();
        Task<string> CanRebootToBootLoaderEntryAsync();

    }
    public class LoginManager : ILoginDaemon
    {
        private ServiceManager ServiceManager { get; set; }
        private Logger Log = LogManager.GetCurrentClassLogger();
        public Dictionary<string, Seat> Seats { get; set; } = new();
        public Dictionary<string, Session> Sessions { get; set; } = new();
        public Dictionary<string, UdevDevice> Devices { get; set; } = new();
        public Dictionary<int, User> Users { get; set; } = new();

        public ObjectPath ObjectPath { get; private set; }

        public LoginManager(ServiceManager serviceManager)
        {
            ServiceManager = serviceManager; 
            
            UdevEnumerator.DeviceAdded += OnDeviceAdded;
            UdevEnumerator.DeviceUpdated += OnDeviceUpdated;

            Seats["seat0"] = new Seat(this, "seat0");
            Seats["seat0"].Save();
            ObjectPath = new ObjectPath("/org/freedesktop/login1");

            if (!Directory.Exists("/run/systemd/seats"))
                Directory.CreateDirectory("/run/systemd/seats");
        }
        
        private User ResolveUserFromMessage(Message message)
        {
            var sender = message.Header.Sender;
            var uid = (int)Convert.ToUInt32(ServiceManager.DBusManager.GetCredentialsByDBusName(sender).Result["UnixUserID"]);

            if (Users.ContainsKey(uid))
            {
                return Users[uid];
            }
            else
            {
                Log.Warn($"Cannot resolve uid of sender {sender}");
                return null;
            }
        }
        private Session ResolveSessionFromMessage(Message message)
        {
            var sender = message.Header.Sender;
            var pid = Convert.ToUInt32(ServiceManager.DBusManager.GetCredentialsByDBusName(sender).Result["ProcessID"]);

            var uid = (int)Convert.ToUInt32(ServiceManager.DBusManager.GetCredentialsByDBusName(sender).Result["UnixUserID"]);

            if (Users.ContainsKey(uid))
            {
                return Sessions[Users[uid].Sessions.Last()];
            }
            else
            {
                Log.Warn($"Cannot resolve session of sender {sender}");
                return null;
            }
        }
        private Seat ResolveSeatFromMessage(Message message)
        {
            var sender = message.Header.Sender;
            var pid = Convert.ToUInt32(ServiceManager.DBusManager.GetCredentialsByDBusName(sender).Result["ProcessID"]);

            var uid = (int)Convert.ToUInt32(ServiceManager.DBusManager.GetCredentialsByDBusName(sender).Result["UnixUserID"]);

            if (Users.ContainsKey(uid))
            {
                var seat_id = Sessions[Users[uid].Sessions.Last()].ActiveSeat;
                if (Seats.ContainsKey(seat_id))
                    return Seats[seat_id];
                return null;
            }
            else
            {
                Log.Warn($"Cannot resolve session of sender {sender}");
                return null;
            }
        }
        
        public async Task RegisterSelfUser()
        {
            await ServiceManager.DBusManager.LoginManagerConnection.RegisterProxiedObjectAsync(
                new ObjectPath("/org/freedesktop/login1/user/auto"), typeof(User), ResolveUserFromMessage);
            await ServiceManager.DBusManager.LoginManagerConnection.RegisterProxiedObjectAsync(
                new ObjectPath("/org/freedesktop/login1/user/self"), typeof(User), ResolveUserFromMessage);
            await ServiceManager.DBusManager.LoginManagerConnection.RegisterProxiedObjectAsync(
                new ObjectPath("/org/freedesktop/login1/session/auto"), typeof(Session), ResolveSessionFromMessage);
            await ServiceManager.DBusManager.LoginManagerConnection.RegisterProxiedObjectAsync(
                new ObjectPath("/org/freedesktop/login1/session/self"), typeof(Session), ResolveSessionFromMessage);
            await ServiceManager.DBusManager.LoginManagerConnection.RegisterProxiedObjectAsync(
                new ObjectPath("/org/freedesktop/login1/seat/auto"), typeof(Seat), ResolveSeatFromMessage);
            await ServiceManager.DBusManager.LoginManagerConnection.RegisterProxiedObjectAsync(
                new ObjectPath("/org/freedesktop/login1/seat/self"), typeof(Seat), ResolveSeatFromMessage);
            await ServiceManager.DBusManager.LoginManagerConnection.RegisterObjectAsync(Seats["seat0"]);
        }

        public void ProcessDeviceTree()
        {
            foreach (var pair in UdevEnumerator.Devices)
                CheckDeviceSeat(pair.Value);
        }

        private void OnDeviceUpdated(object sender, DeviceUpdatedEventArgs e)
        {
            CheckDeviceSeat(UdevEnumerator.Devices[e.DevicePath]);
        }

        private void OnDeviceAdded(object sender, DeviceAddedEventArgs e)
        {
            CheckDeviceSeat(UdevEnumerator.Devices[e.DevicePath]);
        }

        private void CheckDeviceSeat(UdevDevice device)
        {
            //Log.Debug($"Considering {device.SysPath} for inclusion in a seat");
            if (!device.Tags.Contains("seat"))
            {
//                Log.Debug($"Device {device.SysPath} not eligible for inclusion in a seat, not tagged with \"seat\"");
                return;
            }

            var seat_id = device.Properties.ContainsKey("ID_SEAT") ? device.Properties["ID_SEAT"].First() : "seat0";
            var seat = Seats.ContainsKey(seat_id) ? Seats[seat_id] : null;

            var master = device.Tags.Contains("master-of-seat");

            if (seat == null && !master)
            {
//                Log.Debug($"Device {device.SysPath} not eligible for inclusion in a seat, no valid seat ID and not master-of-seat");
                return;
            }

            if (seat == null)
            {
                Log.Debug($"Created new seat {seat_id}");
                Seats[seat_id] = seat = new Seat(this, seat_id);

                if (ServiceManager.DBusManager?.LoginManagerConnection != null)
                {
                    ServiceManager.DBusManager.LoginManagerConnection.RegisterObjectAsync(seat);
                }
            }

            if (seat.Devices.Contains(device.SysPath))
            {
//                Log.Debug($"Device {device.SysPath} already assigned to seat {seat_id}");
            }
            else
            {
                Devices[device.SysPath] = device;
                seat.Devices.Add(device.SysPath);
                seat.Save();
                Log.Debug($"Device {device.SysPath} assigned to seat {seat_id}");
            }
        }

        private int CreateFifoForSession(Session session)
        {
            Directory.CreateDirectory("/run/systemd/sessions");
            
            var path = $"/run/systemd/sessions/{session.SessionId}.ref";
            var ret = Syscall.mkfifo(path, (FilePermissions)448);

            if (ret < 0)
            {
                Log.Error($"Error while creating fifo for session {session.SessionId}: {Syscall.GetLastError()}");
                return -1;
            }

            session.ReaderFd = Syscall.open(path, OpenFlags.O_RDONLY | OpenFlags.O_CLOEXEC | OpenFlags.O_NONBLOCK);
            var fd = Syscall.open(path, OpenFlags.O_WRONLY | OpenFlags.O_CLOEXEC | OpenFlags.O_NONBLOCK);

            if (fd < 0)
            {
                Log.Error($"Error while opening fifo for session {session.SessionId}: {Syscall.GetLastError()}");
            }
            return fd;
        }

        private async Task<User> GetUser(int uid)
        {
            if (Users.ContainsKey(uid))
                return Users[uid];

            return Users[uid] = await CreateUser(uid);
        }

        private async Task<User> CreateUser(int uid)
        {
            var identifier = new UnixUserIdentifier(uid);
            var user = new User(this, uid);

            user.UserName = identifier.Username;
            user.GroupId = (int)identifier.GroupId;

            user.Slice = $"user-{uid}.slice";
            user.Service = $"user@{uid}.service";
            //user.StateFile = $"/run/systemd/users/{uid}";
            user.RuntimePath = $"/run/user/{uid}";

            if (Directory.Exists(user.RuntimePath))
                Log.Info($"User runtime path {user.RuntimePath} already exists, not overwriting it.");
            else
            {
                Directory.CreateDirectory(user.RuntimePath);
                var dir_info = new Mono.Unix.UnixDirectoryInfo(user.RuntimePath);
                dir_info.SetOwner(user.UserId, user.GroupId);
                dir_info.FileAccessPermissions = FileAccessPermissions.UserRead | FileAccessPermissions.UserWrite | FileAccessPermissions.UserExecute;
                
                var mount_unit_file = new GeneratedUnitFile(StringEscaper.EscapePath(user.RuntimePath) + ".mount")
                    .WithProperty("Unit/Description", $"Runtime tmpfs for user {user.UserName}")
                    .WithProperty("Mount/Where", user.RuntimePath)
                    .WithProperty("Mount/Type", "tmpfs")
                    .WithProperty("Mount/Options", $"uid={user.UserId},gid={user.GroupId},mode=700,rw,nosuid,nodev,relatime,seclabel")
                    .WithProperty("Mount/What", "tmpfs");
                
                //Log.Info(JsonConvert.SerializeObject(mount_unit_file));

                Program.ServiceManager.Registry.IndexUnitFile(mount_unit_file);
                var tx = Program.ServiceManager.Planner.CreateActivationTransaction(mount_unit_file.UnitName, "User starting");
                Program.ServiceManager.Runner.Register(tx).Enqueue();
            }

            if (!Directory.Exists(Path.GetDirectoryName(user.StateFile)))
                Directory.CreateDirectory(Path.GetDirectoryName(user.StateFile));
            
            await Program.ServiceManager.DBusManager.LoginManagerConnection.RegisterObjectAsync(user);

            user.Save();
            
            var service_unit = Program.ServiceManager.Registry.GetUnit(user.Service);

            if (service_unit != null)
            {
                var tx = Program.ServiceManager.Planner.CreateActivationTransaction(service_unit, "User starting");
                Program.ServiceManager.Runner.Register(tx).Enqueue();
            }
            else
            {
                Log.Warn($"No per-user service manager instance found ({user.Service})");
            }

            return user;
        }
        
        public async Task<SessionData> CreateSession(SessionRequest request)
        {
            // see https://github.com/systemd/systemd/blob/836fb00f2190811c2bf804860f5afe1160d10eac/src/login/logind-dbus.c#L669

            try
            {

                Log.Debug($"Asked to setup a session");
                Log.Debug(JsonConvert.SerializeObject(request));

                if (string.IsNullOrWhiteSpace(request.seat_id))
                {
                    if (!string.IsNullOrWhiteSpace(request.tty))
                        request.seat_id = "seat0";

                    if (request.tty.StartsWith("/dev"))
                        request.tty = request.tty.Substring("/dev".Length).TrimStart('/');

                    if (request.tty.StartsWith("tty"))
                        request.vtnr = uint.Parse(request.tty.Substring("tty".Length));

                    if (request.tty == "console")
                        request.seat_id = "seat0";
                }

                Log.Debug(JsonConvert.SerializeObject(request));
                //var requesting_user = new UnixUserIdentifier((int) request.uid);
                var user = await GetUser((int) request.uid);

                var session = new Session(this, Sessions.Count.ToString());
                session.UserId = user.UserId;
                session.UserName = user.UserName;
                session.VTNumber = (int) request.vtnr;
                session.TTYPath = request.tty;
                session.LeaderPid = (int) request.pid;
                session.Display = request.display;

                if (Enum.TryParse(typeof(SessionClass), request.@class.Replace("-", ""), true,
                    out object? session_class))
                    session.Class = (SessionClass)session_class;
                
                if (Enum.TryParse(typeof(SessionType), request.type.Replace("-", ""), true,
                    out object? session_type))
                    session.Type = (SessionType)session_type;
                
                session.State = SessionState.Online | SessionState.Active;

                if (request.seat_id == null || !Seats.ContainsKey(request.seat_id))
                {
                    Log.Error($"CreateSession called with seat_id=\"{request.seat_id}\" which does not exist!");
                    session.ActiveSeat = "";
//                    throw new Exception("Oopsie!");
                }
                else
                {
                    session.ActiveSeat = request.seat_id;
                    var seat = Seats[request.seat_id];
                    seat.ActiveSession = session.SessionId;
                    seat.Save();
                }
                
                // Log.Info($"PID is: {Syscall.getpid()}");

                // Syscall.SetSignalAction(Signum.SIGSEGV, SignalAction.Default);

                // var threads = JsonConvert.SerializeObject(Process.GetCurrentProcess().Threads.OfType<ProcessThread>()
                //     .Select(thr =>
                //         new
                //         {
                //             thr.Id, thr.StartAddress, thr.ThreadState, Name = thr.ToString()
                //         }));
                // Log.Debug($"Threads: {threads}");
                
                if (Program.ServiceManager.DBusManager != null)
                {
                    Log.Info($"Registering session {session.SessionId} with dbus service");
                    await Program.ServiceManager.DBusManager.LoginManagerConnection.RegisterObjectAsync(session);
                    Log.Info($"Registered session {session.SessionId} with dbus service");
                }

                Sessions[session.SessionId] = session;
                user.Sessions.Add(session.SessionId);
                user.Save();
                
                //session

                var reply = new SessionData();

                reply.vtnr = (uint) session.VTNumber;
                reply.uid = (uint) session.UserId;
                reply.seat_id = session.ActiveSeat;
                reply.session_id = session.SessionId;
                reply.existing = false;
                reply.object_path = session.ObjectPath;
                reply.runtime_path = user.RuntimePath;

                session.Save();

                if (session.VTNumber > 0)
                {
                    TtyUtilities.Chvt(session.VTNumber);
                }

                //new CloseSafeHandle((IntPtr) CreateFifoForSession(session), false);
                reply.fifo_fd = new CloseSafeHandle((IntPtr)CreateFifoForSession(session), false);

                Log.Debug($"fifo fd: {reply.fifo_fd.DangerousGetHandle()}");
                
                // Create scope for session.
                var scope_name = $"session-{session.SessionId}.scope";

                var scope_unit_file = new GeneratedUnitFile(scope_name)
                    .WithProperty("Description", $"Session {session.SessionId} of user {session.UserName}")
                    .WithProperty("Scope/Slice", $"user-{session.UserId}.slice");

                Program.ServiceManager.Registry.IndexUnitFile(scope_unit_file);
                var scope_unit = Program.ServiceManager.Registry.GetUnit<ScopeUnit>(scope_name);
                var tx = Program.ServiceManager.Planner.CreateActivationTransaction(scope_unit, "Session starting");
                Program.ServiceManager.Runner.Register(tx).Enqueue().Wait();

                scope_unit.CGroup.Join(session.LeaderPid);
                scope_unit.CGroup.Update();
            
                Program.ServiceManager.CGroupManager.RootCGroup.Update();
                Log.Debug($"CreateSession response: {JsonConvert.SerializeObject(reply)}");

                return reply;
            }
            catch (Exception e)
            {
                Log.Error(e);
                throw;
            }
        }

        public async Task<IDictionary<string, object>> GetAllAsync()
        {
            return new Dictionary<string, object>()
            {
                {"RuntimeDirectorySize", 1024 * 1024 * 1024},
                {"InhibitorsMax", 128},
                {"SessionsMax", 1024},
                {"NCurrentSessions", Sessions.Count},
                {"NCurrentInhibitors", 0},
            };
        }

        public async Task<object> GetAsync(string key)
        {
            var dict = await GetAllAsync();
            if (!dict.ContainsKey(key))
                return null;
            return dict[key];
        }

        public async Task<ObjectPath> GetSeatAsync(string seat_id)
        {
            if (!Seats.ContainsKey(seat_id))
            {
                Log.Warn($"Asked for seat {seat_id} which does not exist");
                return null;
            }

            return Seats[seat_id].ObjectPath;
        }

        public async Task<ObjectPath> GetSessionAsync(string session_id, Tmds.DBus.Protocol.Message message)
        {
            var pid_of_sender = await Program.ServiceManager.DBusManager.GetProcessIdByDBusName(message.Header.Sender);
            Log.Info($"Fetching session {session_id}, source pid {pid_of_sender}");
            if (!Sessions.ContainsKey(session_id))
            {
                return null;
            }

            return Sessions[session_id].ObjectPath;
        }

        public async
            Task<(string sessionId, ObjectPath objectPath, string runtimePath, CloseSafeHandle fifoFd, uint uid, string
                seatId, uint vtnr, bool existing)> CreateSessionAsync(uint Uid, uint Pid, string Service, string Type,
                string Class, string Desktop, string SeatId, uint Vtnr, string Tty, string Display, bool Remote,
                string RemoteUser, string RemoteHost, (string, object)[] Properties, Message message)
        {
            var correct_pid = Pid;

            if (correct_pid <= 0)
            {
                var pid_of_dbus_sender = await Program.ServiceManager.DBusManager.GetProcessIdByDBusName(message.Header.Sender);
                if (pid_of_dbus_sender > 0)
                    correct_pid = (uint)pid_of_dbus_sender;
            }

            var resp = await CreateSession(new SessionRequest()
            {
                uid = Uid,
                pid = correct_pid,
                service = Service,
                type =  Type,
                @class = Class,
                desktop =  Desktop,
                seat_id = SeatId,
                vtnr = Vtnr,
                tty = Tty,
                display = Display,
                remote = Remote,
                remote_host = RemoteHost,
                remote_user = RemoteUser,
                properties = Properties.Select(p => new PropertyPair(){Key = p.Item1, Value = p.Item2}).ToArray()
            });

            return (resp.session_id, resp.object_path, resp.runtime_path, resp.fifo_fd, resp.uid, resp.seat_id,
                resp.vtnr, resp.existing);
        }

        public async Task ReleaseSessionAsync(string session_id)
        {
            Log.Debug($"Asked to release session {session_id}");
            
            if (Program.ServiceManager.DBusManager != null && Sessions.ContainsKey(session_id))
            {
                var session = Sessions[session_id];
                Log.Info($"Unregistering session {session.SessionId} with dbus service");
                Program.ServiceManager.DBusManager.LoginManagerConnection.UnregisterObject(session);
                Log.Info($"Unregistered session {session.SessionId} with dbus service");

                if (Users.ContainsKey(session.UserId) && Users[session.UserId].Sessions.Contains(session_id))
                {
                    Users[session.UserId].Sessions.Remove(session_id);
                    Users[session.UserId].Save();
                }

                File.Delete(session.StateFile);
            }
        }

        public async Task<IEnumerable<(string, ObjectPath)>> ListSeatsAsync()
        {
            return Seats.Select(s => (s.Key, s.Value.ObjectPath)).ToList();
        }
        public async Task<IEnumerable<(string, uint, string, string, ObjectPath)>> ListSessionsAsync()
        {
            return Sessions.Select(s => (s.Key, (uint)s.Value.UserId, s.Value.UserName, s.Value.ActiveSeat, s.Value.ObjectPath)).ToList();
        }
        public async Task<IEnumerable<(uint, string, ObjectPath)>> ListUsersAsync()
        {
            return Users.Select(s => ((uint)s.Key, s.Value.UserName, s.Value.ObjectPath)).ToList();
        }

        private Session GetSessionByPID(uint pid)
        {
            var cgroup = File.ReadAllText($"/proc/{pid}/cgroup").Trim();
            var parts = cgroup.Split('/');

            foreach (var part in parts)
            {
                var p = part.Trim();
                if (p.StartsWith("session-") && p.EndsWith(".scope"))
                {
                    var session_id = p.Substring("session-".Length);
                    session_id = session_id.Substring(0, session_id.Length - ".scope".Length);
                    var session_object = Sessions[session_id];
                    Log.Debug($"PID {pid} belongs to session {session_id}");
                    return session_object;
                }
            }

            return null;
        }
        
        public async Task<ObjectPath> GetSessionByPIDAsync(uint pid)
        {
            var session = GetSessionByPID(pid);

            if (session == null)
            {
                Log.Warn($"PID {pid} with cgroup {File.ReadAllText($"/proc/{pid}/cgroup").Trim()} does not belong to any known session");
            }

            return session.ObjectPath;
        }

        public async Task<CloseSafeHandle> InhibitAsync(string what, string who, string why, string mode)
        {
            Syscall.pipe(out int p_r, out int p_w);
            
            Log.Warn($"Call to Inhibit with params ({what}, {who}, {why}, {mode}), ignored as inhibit locks are not yet implemented");
            return new CloseSafeHandle(new IntPtr(p_w), false);
        }

        public async Task<string> CanPowerOffAsync() => "na";
        public async Task<string> CanRebootAsync() => "na";
        public async Task<string> CanHaltAsync() => "na";
        public async Task<string> CanSuspendAsync() => "na";
        public async Task<string> CanHibernateAsync() => "na";
        public async Task<string> CanHybridSleepAsync() => "na";
        public async Task<string> CanSuspendThenHibernateAsync() => "na";
        public async Task<string> CanRebootParameterAsync() => "na";
        public async Task<string> CanRebootToFirmwareSetupAsync() => "na";
        public async Task<string> CanRebootToBootLoaderMenuAsync() => "na";
        public async Task<string> CanRebootToBootLoaderEntryAsync() => "na";
    }
}