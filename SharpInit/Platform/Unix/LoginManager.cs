using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using Mono.Unix;
using Mono.Unix.Native;
using Newtonsoft.Json;
using NLog;
using NLog.Fluent;
using SharpInit.LoginManager;
using SharpInit.Units;
using Tmds.DBus;

namespace SharpInit.Platform.Unix
{
    public class LoginManager : ILoginDaemon
    {
        private Logger Log = LogManager.GetCurrentClassLogger();
        public Dictionary<string, Seat> Seats { get; set; } = new();
        public Dictionary<string, Session> Sessions { get; set; } = new();
        public Dictionary<int, User> Users { get; set; } = new();

        public ObjectPath ObjectPath { get; private set; }

        public LoginManager()
        {
            UdevEnumerator.DeviceAdded += OnDeviceAdded;
            UdevEnumerator.DeviceUpdated += OnDeviceUpdated;

            Seats["seat0"] = new Seat("seat0");
            ObjectPath = new ObjectPath("/org/freedesktop/login1");

            if (!Directory.Exists("/run/systemd/seats"))
                Directory.CreateDirectory("/run/systemd/seats");
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
            Log.Debug($"Considering {device.SysPath} for inclusion in a seat");
            if (!device.Tags.Contains("seat"))
            {
                Log.Debug($"Device {device.SysPath} not eligible for inclusion in a seat, not tagged with \"seat\"");
                return;
            }

            var seat_id = device.Properties.ContainsKey("ID_SEAT") ? device.Properties["ID_SEAT"].First() : "seat0";
            var seat = Seats.ContainsKey(seat_id) ? Seats[seat_id] : null;

            var master = device.Tags.Contains("master-of-seat");

            if (seat == null && !master)
            {
                Log.Debug($"Device {device.SysPath} not eligible for inclusion in a seat, no valid seat ID and not master-of-seat");
                return;
            }

            if (seat == null)
            {
                Log.Debug($"Created new seat {seat_id}");
                Seats[seat_id] = seat = new Seat(seat_id);
            }

            if (seat.Devices.Contains(device.SysPath))
            {
                Log.Debug($"Device {device.SysPath} already assigned to seat {seat_id}");
            }
            else
            {
                seat.Devices.Add(device.SysPath);
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

        private User GetUser(int uid)
        {
            if (Users.ContainsKey(uid))
                return Users[uid];

            return Users[uid] = CreateUser(uid);
        }

        private User CreateUser(int uid)
        {
            var identifier = new UnixUserIdentifier(uid);
            var user = new User(uid);

            user.UserName = identifier.Username;
            user.GroupId = (int)identifier.GroupId;

            user.Slice = $"user-{uid}.slice";
            user.Service = $"user@{uid}.service";
            user.StateFile = $"/run/systemd/users/{uid}";
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
                
                Log.Info(JsonConvert.SerializeObject(mount_unit_file));

                Program.ServiceManager.Registry.IndexUnitFile(mount_unit_file);
                var tx = Program.ServiceManager.Planner.CreateActivationTransaction(mount_unit_file.UnitName, "User starting");
                Program.ServiceManager.Runner.Register(tx).Enqueue();
            }

            if (!Directory.Exists(Path.GetDirectoryName(user.StateFile)))
                Directory.CreateDirectory(Path.GetDirectoryName(user.StateFile));
            
            File.WriteAllText(user.StateFile, $"NAME={user.UserName}");
            
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
                var user = GetUser((int) request.uid);

                var session = new Session(Sessions.Count.ToString());
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
                
                session.State = SessionState.Online;

                if (!Seats.ContainsKey(request.seat_id))
                {
                    Log.Error($"CreateSession called with seat_id=\"{request.seat_id}\" which does not exist!");
                    throw new Exception("Oopsie!");
                }

                session.ActiveSeat = request.seat_id;
                var seat = Seats[request.seat_id];
                seat.ActiveSession = session.SessionId;
                
                Log.Info($"PID is: {Syscall.getpid()}");

                Syscall.SetSignalAction(Signum.SIGSEGV, SignalAction.Default);

                var threads = JsonConvert.SerializeObject(Process.GetCurrentProcess().Threads.OfType<ProcessThread>()
                    .Select(thr =>
                        new
                        {
                            thr.Id, thr.StartAddress, thr.ThreadState, Name = thr.ToString()
                        }));
                Log.Debug($"Threads: {threads}");
                
                if (Program.ServiceManager.DBusManager != null)
                {
                    Log.Info($"Registering session {session.SessionId} with dbus service");
                    await Program.ServiceManager.DBusManager.Connection.RegisterObjectAsync(session);
                    Log.Info($"Registered session {session.SessionId} with dbus service");
                }

                user.Sessions.Add(session.SessionId);

                var reply = new SessionData();

                reply.vtnr = (uint) session.VTNumber;
                reply.uid = (uint) session.UserId;
                reply.seat_id = session.ActiveSeat;
                reply.session_id = session.SessionId;
                reply.existing = false;
                reply.object_path = session.ObjectPath;
                reply.runtime_path = user.RuntimePath;

                //new CloseSafeHandle((IntPtr) CreateFifoForSession(session), false);
                reply.fifo_fd = new CloseSafeHandle((IntPtr)CreateFifoForSession(session), false);

                Log.Debug($"fifo fd: {reply.fifo_fd.DangerousGetHandle()}");
                
                Log.Debug($"CreateSession response: {JsonConvert.SerializeObject(reply)}");

                return reply;
            }
            catch (Exception e)
            {
                Log.Error(e);
                throw;
            }
        }

        public async
            Task<(string sessionId, ObjectPath objectPath, string runtimePath, CloseSafeHandle fifoFd, uint uid, string
                seatId, uint vtnr, bool existing)> CreateSessionAsync(uint Uid, uint Pid, string Service, string Type,
                string Class, string Desktop, string SeatId, uint Vtnr, string Tty, string Display, bool Remote,
                string RemoteUser, string RemoteHost, (string, object)[] Properties)
        {
            var resp = await CreateSession(new SessionRequest()
            {
                uid = Uid,
                pid = Pid,
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
        }
    }
}