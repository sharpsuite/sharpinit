using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Tmds.DBus;

using NLog;
using DBus.DBus;
using System;
using SharpInit.Units;

namespace SharpInit
{
    public class DBusManager
    {
        Logger Log = LogManager.GetCurrentClassLogger();

        public ServiceManager ServiceManager { get; set; }
        public Connection Connection { get; set; }

        public List<string> AcquiredNames { get; set; } = new();

        public Dictionary<string, Unit> AssociatedUnits { get; set; } = new();

        IDBus DBusProxy { get; set; }
        IDisposable NameWatcher { get; set; }

        public DBusManager(ServiceManager manager)
        {
            ServiceManager = manager;
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

            Log.Info($"Connecting to dbus...");
            Connection = new Connection(Address.System);

            await Connection.ConnectAsync();

            Log.Info($"Connected to dbus.");
            await Connection.RegisterServiceAsync("org.sharpinit.sharpinit", options: ServiceRegistrationOptions.AllowReplacement | ServiceRegistrationOptions.ReplaceExisting | ServiceRegistrationOptions.Default);
            
            DBusProxy = Connection.CreateProxy<DBus.DBus.IDBus>("org.freedesktop.DBus", "/org/freedesktop/DBus");
            var rule = new Tmds.DBus.SignalMatchRule()
            {
                Path = "/org/freedesktop/DBus",
                Interface = "org.freedesktop.DBus",
                Member = "NameOwnerChanged"
            };

            NameWatcher = await Connection.WatchSignalAsync<(string, string, string)>(rule, p => { OnNameAcquired(p.args); });

            var services = await Connection.ListServicesAsync();
            foreach (var name in services)
            {
                Log.Debug($"Discovered dbus service {name}");
                AcquiredNames.Add(name);
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