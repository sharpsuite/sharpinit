using System;
using System.Collections.Generic;
using System.Text;

namespace SharpInit.Units
{
    /// <summary>
    /// Common denominator unit type for .service, .socket, .swap and .mount
    /// </summary>
    public class ExecUnitDescriptor : UnitDescriptor
    {
        [UnitProperty("@/@", UnitPropertyType.String)]
        public string WorkingDirectory { get; set; }

        [UnitProperty("@/RootDirectory", UnitPropertyType.String)]
        public string RootDirectory { get; set; }

        [UnitProperty("@/RootImage", UnitPropertyType.String)]
        public string RootImage { get; set; }

        [UnitProperty("@/RuntimeDirectory", UnitPropertyType.String)]
        public string RuntimeDirectory { get; set; }

        [UnitProperty("@/StateDirectory", UnitPropertyType.String)]
        public string StateDirectory { get; set; }

        [UnitProperty("@/CacheDirectory", UnitPropertyType.String)]
        public string CacheDirectory { get; set; }

        [UnitProperty("@/LogsDirectory", UnitPropertyType.String)]
        public string LogsDirectory { get; set; }

        [UnitProperty("@/ConfigurationDirectory", UnitPropertyType.String)]
        public string ConfigurationDirectory { get; set; }


        [UnitProperty("@/User", UnitPropertyType.String)]
        public string User { get; set; }

        [UnitProperty("@/Group", UnitPropertyType.String)]
        public string Group { get; set; }

        [UnitProperty("@/DynamicUser", UnitPropertyType.Bool, false)]
        public bool DynamicUser { get; set; }

        [UnitProperty("@/SupplementaryGroups", UnitPropertyType.StringListSpaceSeparated)]
        public List<string> SupplementaryGroups { get; set; }
        
        [UnitProperty("@/PAMName", UnitPropertyType.String)]
        public string PAMName { get; set; }


        [UnitProperty("@/Capabilities", UnitPropertyType.StringListSpaceSeparated)]
        public List<string> Capabilities { get; set; }

        [UnitProperty("@/AmbientCapabilities", UnitPropertyType.StringListSpaceSeparated)]
        public List<string> AmbientCapabilities { get; set; }


        [UnitProperty("@/StandardInput", UnitPropertyType.String, "null")]
        public string StandardInput { get; set; }

        [UnitProperty("@/StandardOutput", UnitPropertyType.String, "journal")]
        public string StandardOutput { get; set; }

        [UnitProperty("@/StandardError", UnitPropertyType.String, "journal")]
        public string StandardError { get; set; }


        [UnitProperty("@/@", UnitPropertyType.Time, "5")]
        public TimeSpan TimeoutSec { get; set; }

        [UnitProperty("@/Environment", UnitPropertyType.StringListSpaceSeparated)]
        public List<string> Environment { get; set; }

        [UnitProperty("@/@")]
        public string TtyPath { get; set; }

        [UnitProperty("@/@", UnitPropertyType.Bool)]
        public bool TtyReset { get; set; }
        [UnitProperty("@/@", UnitPropertyType.Bool)]
        public bool TtyVHangup { get; set; }
        [UnitProperty("@/@", UnitPropertyType.Bool)]
        public bool TtyVtDisallocate { get; set; }
        
        
        [UnitProperty("@/@", UnitPropertyType.String, "system.slice")]
        public string Slice { get; set; }

        [UnitProperty("@/@", UnitPropertyType.Enum, KillMode.ControlGroup, typeof(KillMode))]
        public KillMode KillMode { get; set; }

        [UnitProperty("@/@", UnitPropertyType.Signal, 15)] // SIGTERM
        public int KillSignal { get; set; }

        [UnitProperty("@/@", UnitPropertyType.Signal, -1)]
        public int RestartKillSignal { get; set; }

        [UnitProperty("@/@", UnitPropertyType.Signal, 9)] // SIGKILL
        public int FinalKillSignal { get; set; }

        [UnitProperty("@/@", UnitPropertyType.Signal, 6)] // SIGABRT
        public int WatchdogKillSignal { get; set; }
        
        [UnitProperty("@/@", UnitPropertyType.Bool, false)]
        public bool SendSighup { get; set; }

        [UnitProperty("@/@", UnitPropertyType.Bool, true)]
        public bool SendSigkill { get; set; }
    }

    public enum KillMode
    {
        ControlGroup,
        Mixed,
        Process,
        None
    }
}
