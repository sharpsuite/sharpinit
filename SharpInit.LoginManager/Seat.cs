using System.Collections.Generic;
using System.Threading.Tasks;
using Tmds.DBus;

namespace SharpInit.LoginManager
{
    [DBusInterface("org.freedesktop.login1.Seat")]
    public interface ISeat : IDBusObject
    {
        Task<IDictionary<string, object>> GetAllAsync();
        Task<object> GetAsync(string key);
        Task SwitchToAsync(uint vt);
    }
}