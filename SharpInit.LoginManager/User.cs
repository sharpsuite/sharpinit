using System.Collections.Generic;
using Tmds.DBus;

namespace SharpInit.LoginManager
{
    public class User : IDBusObject
    {
        
        public int UserId { get; set; }
        public int GroupId { get; set; }
        public string UserName { get; set; }
        public string Service { get; set; }
        public string Slice { get; set; }
        public string StateFile { get; set; }
        public string RuntimePath { get; set; }

        public List<string> Sessions { get; set; } = new();
        public ObjectPath ObjectPath { get; private set; }

        public User(int uid)
        {
            UserId = uid;
            ObjectPath = new ObjectPath($"/org/freedesktop/login1/User/{uid}");
        }
    }
}