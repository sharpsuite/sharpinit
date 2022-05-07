using System.Collections.Generic;
using System.Threading.Tasks;
using Tmds.DBus;

namespace SharpInit.LoginManager
{
    [DBusInterface("org.freedesktop.login1.User")]
    public interface IUser : IDBusObject
    {
        Task<IDictionary<string, object>> GetAllAsync();
        Task<object> GetAsync(string key);
        // public int UserId { get; }
        // public int GroupId { get; }
        // public string UserName { get; }
        // public string Service { get; }
        // public string Slice { get; }
        // public string StateFile { get; }
        // public string RuntimePath { get; }
    }
}