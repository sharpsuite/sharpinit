using System.Collections.Generic;
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
        
        public Seat(LoginManager manager, string seat_id)
        {
            LoginManager = manager;
            SeatId = seat_id;
            ObjectPath = new ObjectPath($"/org/freedesktop/login1/Seat/{seat_id}");
        }

        public ObjectPath ObjectPath { get; private set; }
    }
}