using System.Threading.Tasks;
using Tmds.DBus;

namespace SharpInit.LoginManager
{

    [DBusInterface("org.freedesktop.login1.Session")]
    public interface ISession : IDBusObject
    {
        public Task ActivateAsync();
        public Task TakeControlAsync(bool force);
        public Task ReleaseControlAsync();
        public Task<(CloseSafeHandle, bool)> TakeDeviceAsync(uint major, uint minor);
        public Task ReleaseDeviceAsync(uint major, uint minor);
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