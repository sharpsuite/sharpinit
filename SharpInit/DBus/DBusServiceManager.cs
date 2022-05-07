using System.Collections.Generic;
using System.Threading.Tasks;
using Tmds.DBus;

namespace SharpInit.DBus
{
    [DBusInterface("org.freedesktop.systemd1.Manager")]
    public interface IDBusServiceManager : IDBusObject
    {
        Task SetEnvironmentAsync(string[] assignments);
        Task UnsetEnvironmentAsync(string[] assignments);
        Task UnsetAndSetEnvironmentAsync(string[] unset, string[] set);
        Task<IDictionary<string, object>> GetAllAsync();
        Task<object> GetAsync(string key);
    }
    
    public class DBusServiceManager : IDBusServiceManager
    {
        private ServiceManager ServiceManager;
        public ObjectPath ObjectPath { get; }

        public DBusServiceManager(ServiceManager serviceManager)
        {
            ServiceManager = serviceManager;
            ObjectPath = new ObjectPath("/org/freedesktop/systemd1");
        }
        
        public async Task SetEnvironmentAsync(string[] assignments)
        {
            foreach (var assignment in assignments)
            {
                var equalsIndex = assignment.IndexOf('=');

                if (equalsIndex == -1)
                {
                    ServiceManager.DefaultActivationEnvironment[assignment] = "";
                    continue;
                }

                var key = assignment.Substring(0, equalsIndex);
                var value = assignment.Substring(equalsIndex + 1);

                ServiceManager.DefaultActivationEnvironment[assignment] = value;
            }
        }

        public async Task UnsetEnvironmentAsync(string[] keys)
        {
            foreach (var key in keys)
            {
                ServiceManager.DefaultActivationEnvironment.TryRemove(key, out string _);
            }
        }

        public async Task UnsetAndSetEnvironmentAsync(string[] unset, string[] set)
        {
            await UnsetEnvironmentAsync(unset);
            await SetEnvironmentAsync(set);
        }
        
        public async Task<IDictionary<string, object>> GetAllAsync()
        {
            var ret = new Dictionary<string, object>();
            return ret;
        }

        public async Task<object> GetAsync(string key)
        {
            switch (key)
            {
                default:
                    return null;
            }
        }
    }
}