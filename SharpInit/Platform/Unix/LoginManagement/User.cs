using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NLog.Fluent;
using SharpInit.LoginManager;
using Tmds.DBus;

namespace SharpInit.Platform.Unix.LoginManagement
{
    public class User : IUser
    {
        private LoginManager LoginManager;
        public int UserId { get; set; }
        public int GroupId { get; set; }
        public string UserName { get; set; }
        public string Service { get; set; }
        public string Slice { get; set; }
        public string StateFile { get; set; }
        public string RuntimePath { get; set; }

        public List<string> Sessions { get; set; } = new();
        public ObjectPath ObjectPath { get; private set; }

        private DateTime _loggedIn;

        public User(LoginManager loginManager, int uid)
        {
            LoginManager = loginManager;
            UserId = uid;
            ObjectPath = new ObjectPath($"/org/freedesktop/login1/User/{uid}");
            StateFile = $"/run/systemd/users/{uid}";
            RuntimePath = $"/run/user/{uid}";
            _loggedIn = DateTime.UtcNow;
        }

        public void Save()
        {
            var user_file_contents = new StringBuilder();

            var valid_session_ids = Sessions.Where(s => LoginManager.Sessions.ContainsKey(s)).ToList(); 
            var valid_sessions = valid_session_ids.Select(s => LoginManager.Sessions[s]).ToList();
            
            user_file_contents.AppendLine($"NAME={UserName}");
            user_file_contents.AppendLine($"SESSIONS={string.Join(' ', valid_session_ids)}");
            user_file_contents.AppendLine($"ACTIVE_SESSIONS={string.Join(' ', valid_sessions.Where(s => s.State == SessionState.Active).Select(s => s.SessionId))}");
            user_file_contents.AppendLine($"ONLINE_SESSIONS={string.Join(' ', valid_sessions.Where(s => s.State == SessionState.Online).Select(s => s.SessionId))}");
            
            user_file_contents.AppendLine($"SEATS={string.Join(' ', valid_sessions.Select(s => s.ActiveSeat))}");
            user_file_contents.AppendLine($"ACTIVE_SEATS={string.Join(' ', valid_sessions.Where(s => s.State == SessionState.Active).Select(s => s.ActiveSeat))}");
            user_file_contents.AppendLine($"ONLINE_SEATS={string.Join(' ', valid_sessions.Where(s => s.State == SessionState.Online).Select(s => s.ActiveSeat))}");

            File.WriteAllText(StateFile, user_file_contents.ToString());
        }

        public async Task<IDictionary<string, object>> GetAllAsync()
        {
            var activeSession = Sessions.Where(s => LoginManager.Sessions.ContainsKey(s)).First();
            var activeSessionPath = LoginManager.Sessions[activeSession].ObjectPath;

            var sessions = Sessions.Where(s => LoginManager.Sessions.ContainsKey(s))
                .Select(s => (s, LoginManager.Sessions[s].ObjectPath)).ToArray();
            
            return new Dictionary<string, object>()
            {
                {"UID", UserId},
                {"GID", GroupId},
                {"Name", UserName},
                {"RuntimePath", RuntimePath},
                {"Service", $"user@{UserId}.service"},
                {"Slice", $"user-{UserId}.slice"},
                {"State", "active"},
                {"Display", (activeSession, activeSessionPath)},
                {"Sessions", sessions},
                {"IdleHint", false},
                {"Linger", true},
                {"Timestamp", (long)((_loggedIn - DateTime.UnixEpoch).TotalMilliseconds * 1000)},
                {"TimestampMonotonic", (long)((_loggedIn - DateTime.UnixEpoch).TotalMilliseconds * 1000)},
            };
        }

        public async Task<object> GetAsync(string key)
        {
            var dict = await GetAllAsync();
            if (!dict.ContainsKey(key))
                return null;
            return dict[key];
        }
    }
}