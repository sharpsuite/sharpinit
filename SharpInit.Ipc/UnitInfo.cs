using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SharpInit.Ipc
{
    public class UnitInfo
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string Path { get; set; }
        public string[] Documentation { get; set; }
        public UnitState CurrentState { get; set; }
        public UnitState PreviousState { get; set; }
        public DateTime LastStateChangeTime { get; set; }
        public DateTime ActivationTime { get; set; }
        public DateTime LoadTime { get; set; }

        public string StateChangeReason { get; set; }

        public List<string> LogLines { get; set; }

        public UnitInfo()
        {

        }
    }


    public enum UnitState
    {
        Inactive = 0x1,
        Active = 0x2,
        Activating = 0x4,
        Deactivating = 0x8,
        Failed = 0x10,
        Reloading = 0x20,
        Any = 0x7FFFFFFF
    }
}
