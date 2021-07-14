using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Tmds.DBus;

namespace SharpInit
{
    public class DbusManager
    {
        public ServiceManager ServiceManager { get; set; }
        public Connection Connection { get; set; }

        public List<string> AcquiredNames { get; set; }

        public DbusManager(ServiceManager manager)
        {
            ServiceManager = manager;
            AcquiredNames = new List<string>();
            NameAcquiredCts = new CancellationTokenSource();
        }

        public async Task Connect()
        {
            if (Connection != null)
                return;

            Connection = new Connection(Address.System);
            await Connection.ConnectAsync();

            var dbus_proxy = Connection.CreateProxy<DBus.DBus.IDBus>("org.freedesktop.DBus", "/org/freedesktop/DBus");

            foreach (var name in (await Connection.ListServicesAsync()))
            {
                AcquiredNames.Add(name);
            }

            await dbus_proxy.WatchNameAcquiredAsync(OnNameAcquired);
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

        public void OnNameAcquired(string name)
        {
            if (!AcquiredNames.Contains(name))
                AcquiredNames.Add(name);
            
            var old_cts = NameAcquiredCts;

            NameAcquiredCts = new CancellationTokenSource();
            old_cts.Cancel();
        }
    }
}