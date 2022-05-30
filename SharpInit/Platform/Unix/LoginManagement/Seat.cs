using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SharpInit.LoginManager;
using Tmds.DBus;

namespace SharpInit.Platform.Unix.LoginManagement
{
    public class Seat : ISeat
    {
        private LoginManager LoginManager;
        public string SeatId { get; set; }
        public string ActiveSession { get; set; }
        public bool CanTTY { get; set; }
        public bool CanGraphical { get; set; }
        public List<string> Sessions { get; set; } = new();
        public List<string> Devices { get; set; } = new();
        public string StateFile { get; set; }
        
        public Seat(LoginManager manager, string seat_id)
        {
            LoginManager = manager;
            SeatId = seat_id;
            ObjectPath = new ObjectPath($"/org/freedesktop/login1/seat/{seat_id}");
            StateFile = $"/run/systemd/seats/{seat_id}";
        }

        public void Save()
        {
            CanTTY = true;
            CanGraphical = Devices.Any(d => LoginManager.Devices.ContainsKey(d) && LoginManager.Devices[d].Tags.Contains("master-of-seat"));
            
            var seat_file_contents = new StringBuilder();
            seat_file_contents.AppendLine($"IS_SEAT0={(SeatId == "seat0" ? 1 : 0)}");
            seat_file_contents.AppendLine($"CAN_MULTI_SESSION=1");
            seat_file_contents.AppendLine($"CAN_TTY={(CanTTY ? 1 : 0)}");
            seat_file_contents.AppendLine($"CAN_GRAPHICAL={(CanGraphical ? 1 : 0)}");

            Directory.CreateDirectory(Path.GetDirectoryName(StateFile));
            File.WriteAllText(StateFile, seat_file_contents.ToString());
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

        public async Task SwitchToAsync(uint vt)
        {
            TtyUtilities.Chvt((int)vt);
            if (LoginManager.Sessions.ContainsKey(ActiveSession))
            {
                LoginManager.Sessions[ActiveSession].VTNumber = (int)vt;
            }
        }

        public ObjectPath ObjectPath { get; private set; }
    }
}