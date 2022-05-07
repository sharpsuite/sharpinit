using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Tmds.DBus;

using NLog;
using DBus.DBus;
using System;
using System.IO;
using SharpInit.DBus;
using SharpInit.Units;

namespace SharpInit
{
    public class DBusManager
    {
        Logger Log = LogManager.GetCurrentClassLogger();

        public DBusServiceManager DBusServiceManager { get; set; }
        public ServiceManager ServiceManager { get; set; }
        public Connection Connection { get; set; }
        public Connection LoginManagerConnection { get; set; }
        public Connection ServiceManagerConnection { get; set; }

        public List<string> AcquiredNames { get; set; } = new();

        public Dictionary<string, Unit> AssociatedUnits { get; set; } = new();

        IDBus DBusProxy { get; set; }
        IDisposable NameWatcher { get; set; }
        private string ConnectionAddress => Program.IsUserManager ? Address.Session : Address.System;

        public DBusManager(ServiceManager manager)
        {
            ServiceManager = manager;
            DBusServiceManager = new DBusServiceManager(manager);
            NameAcquiredCts = new CancellationTokenSource();
        }

        public void Associate(string bus_name, Unit unit)
        {
            AssociatedUnits[bus_name] = unit;
        }

        public async Task Connect()
        {
            if (Connection != null)
                return;

            try
            {
                //var dbus_socket_path = "/run/dbus/system_bus_socket";
                var dbus_socket_path = ConnectionAddress.Split("path=")[1];

                if (!File.Exists(dbus_socket_path))
                {
                    Log.Info($"Waiting for dbus socket at {dbus_socket_path}");

                    while (!File.Exists(dbus_socket_path))
                        await Task.Delay(500);
                }

                Log.Info($"Connecting to dbus at at {dbus_socket_path}...");
                Connection = new Connection(ConnectionAddress);

                await Connection.ConnectAsync();

                Log.Info($"Connected to dbus.");
                await Connection.RegisterServiceAsync("org.sharpinit.sharpinit",
                    options: ServiceRegistrationOptions.AllowReplacement | ServiceRegistrationOptions.ReplaceExisting |
                             ServiceRegistrationOptions.Default);

                DBusProxy = Connection.CreateProxy<IDBus>("org.freedesktop.DBus", "/org/freedesktop/DBus");
                var rule = new Tmds.DBus.SignalMatchRule()
                {
                    Path = "/org/freedesktop/DBus",
                    Interface = "org.freedesktop.DBus",
                    Member = "NameOwnerChanged"
                };

                if (Program.LoginManager != null)
                {
                    await SetupLoginService();
                }

                if (DBusServiceManager != null)
                {
                    await SetupServiceManager();
                }

                NameWatcher =
                    await Connection.WatchSignalAsync<(string, string, string)>(rule, p => { OnNameAcquired(p.args); });

                var services = await Connection.ListServicesAsync();
                foreach (var name in services)
                {
                    Log.Debug($"Discovered dbus service {name}");
                    AcquiredNames.Add(name);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Exception thrown while connecting to D-Bus", ex);
                throw;
            }
        }

        public async Task<IDictionary<string, object>> GetCredentialsByDBusName(string sender)
        {
            var result = await DBusProxy.GetConnectionCredentialsAsync(sender);
            return result;
        }

        public async Task<int> GetProcessIdByDBusName(string sender)
        {
            var result = await GetCredentialsByDBusName(sender);

            if (result == null)
                return -1;
            
            if (!result.ContainsKey("ProcessID"))
                return -1;

            return Convert.ToInt32((uint) result["ProcessID"]);
        }

        public async Task SetupLoginService()
        {
            if (LoginManagerConnection != null)
                return;

            try
            {
                LoginManagerConnection = new Connection(ConnectionAddress);
                await LoginManagerConnection.ConnectAsync();
                await LoginManagerConnection.RegisterServiceAsync("org.freedesktop.login1",
                    ServiceRegistrationOptions.ReplaceExisting);
                await LoginManagerConnection.RegisterObjectAsync(Program.LoginManager);
                await Program.LoginManager.RegisterSelfUser();

                Log.Debug($"Registered logind on DBus");
            }
            catch (Exception ex)
            {
                Log.Error($"Exception thrown while registering login daemon on D-Bus");
                Log.Error(ex);
            }
        }
        
        public async Task SetupServiceManager()
        {
            if (ServiceManagerConnection != null)
                return;

            try
            {
                ServiceManagerConnection = new Connection(ConnectionAddress);
                await ServiceManagerConnection.ConnectAsync();
                await ServiceManagerConnection.RegisterServiceAsync("org.freedesktop.systemd1",
                    ServiceRegistrationOptions.ReplaceExisting);
                await ServiceManagerConnection.RegisterObjectAsync(DBusServiceManager);

                Log.Debug($"Registered service manager on DBus");
            }
            catch (Exception ex)
            {
                Log.Error($"Exception thrown while registering service manager on D-Bus");
                Log.Error(ex);
            }
        }

        public async Task<bool> WaitForBusName(string bus_name, int timeout = -1)
        {
            if (AcquiredNames.Contains(bus_name))
                return true;
            
            var time = Program.ElapsedSinceStartup();
            
            while (true)
            {
                var token = NameAcquiredCts.Token;
                await Task.Delay(timeout, token).ContinueWith(t => {});

                if (AcquiredNames.Contains(bus_name))
                    return true;
                
                if ((Program.ElapsedSinceStartup() - time).TotalMilliseconds > timeout)
                    return false;
            }
        }

        private CancellationTokenSource NameAcquiredCts { get; set; }

        public void OnNameAcquired(ValueTuple<string, string, string> names)
        {
            (string name, string old_owner, string new_owner) = names;
            
            if (string.IsNullOrWhiteSpace(new_owner))
            {
                if (AssociatedUnits.ContainsKey(name))
                {
                    AssociatedUnits[name].RaiseBusNameReleased(name, old_owner);
                    AssociatedUnits.Remove(name);
                }
                
                return;
            }

            if (!AcquiredNames.Contains(name))
                AcquiredNames.Add(name);
            
            var old_cts = NameAcquiredCts;

            NameAcquiredCts = new CancellationTokenSource();
            old_cts.Cancel();
        }
    }
}