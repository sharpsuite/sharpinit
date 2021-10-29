using Tmds.DBus;

namespace SharpInit.LoginManager
{
    public class Session : IDBusObject
    {
        public ObjectPath ObjectPath { get; internal set; }
        public string SessionId { get; set; }
        public int UserId { get; set; }
        public string UserName { get; set; }
        public SessionClass Class { get; set; }
        public SessionType Type { get; set; }
        public SessionState State { get; set; }
        public int VTNumber { get; set; }
        public string TTYPath { get; set; }
        public string Display { get; set; }
        public int LeaderPid { get; set; }
        public string ActiveSeat { get; set; }
        public int ReaderFd { get; set; }

        public Session(string session_id)
        {
            SessionId = session_id;
            ObjectPath = new ObjectPath($"/org/sharpinit/login1/session/{session_id}");
        }
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