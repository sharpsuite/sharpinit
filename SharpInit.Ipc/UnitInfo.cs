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
        public UnitState State { get; set; }
        public DateTime LastStateChangeTime { get; set; }
        public DateTime ActivationTime { get; set; }
        public DateTime LoadTime { get; set; }

        public List<string> LogLines { get; set; }

        public UnitInfo()
        {

        }
    }

    public enum UnitState
    {
        Inactive,
        Active,
        Activating,
        Deactivating,
        Failed,
        Reloading,
        Any // used as a special mask
    }
}
