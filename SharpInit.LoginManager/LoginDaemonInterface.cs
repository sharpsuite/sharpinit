using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using Tmds.DBus;
using Tmds.DBus.Protocol;

namespace SharpInit.LoginManager
{
    [StructLayout(LayoutKind.Sequential)]
    public struct PropertyPair
    {
        public string Key;
        public object Value;
    }
    
    // uusssssussbssa(sv)
    [StructLayout(LayoutKind.Sequential)]
    public struct SessionRequest
    {
        public uint uid;
        public uint pid;
        public string service;
        public string type;
        public string @class;
        public string desktop;
        public string seat_id;
        public uint vtnr;
        public string tty;
        public string display;
        public bool remote;
        public string remote_user;
        public string remote_host;
        public PropertyPair[] properties; // a(sv)
    }
    
    [StructLayout(LayoutKind.Sequential)]
    public struct SessionData
    {
        public string session_id;
        public ObjectPath object_path;
        public string runtime_path;
        public CloseSafeHandle fifo_fd;
        public uint uid;
        public string seat_id;
        public uint vtnr;
        public bool existing;
    }

    [DBusInterface("org.freedesktop.login1.Manager")]
    public interface ILoginDaemon : IDBusObject
    {
        Task<ObjectPath> GetSeatAsync(string seat_id);
        Task<ObjectPath> GetSessionAsync(string session_id, Tmds.DBus.Protocol.Message message);
        
        Task<(string sessionId, ObjectPath objectPath, string runtimePath, CloseSafeHandle fifoFd, uint uid, string seatId, uint vtnr, bool existing)> CreateSessionAsync(uint Uid, uint Pid
            , string Service, string Type, string Class, string Desktop, string SeatId, uint Vtnr, string 
                Tty, string Display, bool Remote, string RemoteUser, string RemoteHost, (string, object)[] Properties, Message message);
        public Task ReleaseSessionAsync(string session_id);
    }
}