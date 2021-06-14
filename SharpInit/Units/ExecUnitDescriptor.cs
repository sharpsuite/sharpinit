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
    }
}
