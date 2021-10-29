using System.Collections.Generic;

namespace SharpInit.LoginManager
{
    public class Seat
    {
        public string SeatId { get; set; }
        public string ActiveSession { get; set; }
        public bool CanTTY { get; set; }
        public bool CanGraphical { get; set; }
        public List<string> Sessions { get; set; } = new();
        public List<string> Devices { get; set; } = new();
        
        public Seat(string seat_id)
        {
            SeatId = seat_id;
        }
    }
}