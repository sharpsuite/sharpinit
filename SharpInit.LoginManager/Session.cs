using System.Collections.Generic;
using System.Threading.Tasks;
using Tmds.DBus;

namespace SharpInit.LoginManager
{

    [DBusInterface("org.freedesktop.login1.Session")]
    public interface ISession : IDBusObject
    {
        Task<IDictionary<string, object>> GetAllAsync();
        Task<object> GetAsync(string key);
        public Task ActivateAsync();
        public Task TakeControlAsync(bool force);
        public Task ReleaseControlAsync();
        public Task<(CloseSafeHandle, bool)> TakeDeviceAsync(uint major, uint minor);
        public Task ReleaseDeviceAsync(uint major, uint minor);
        public Task SetTypeAsync(string type);
        public Task SetBrightnessAsync(string subsystem, string name, uint brightness);
    }

    public enum SessionClass
    {
        User,
        Greeter,
        LockScreen
    }

    public enum SessionType
    {
        Unspecified,
        Tty,
        X11,
        Wayland,
        Mir
    }

    public enum SessionState
    {
        Online,
        Active,
        Closing
    }
}