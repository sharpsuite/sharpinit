using System;
using System.Collections.Generic;
using System.Text;

namespace SharpInit.Units
{
    public class ServiceUnitDescriptor : ExecUnitDescriptor
    {
        [UnitProperty("Service/Type", UnitPropertyType.Enum, ServiceType.Simple, typeof(ServiceType))]
        public ServiceType ServiceType { get; set; }

        [UnitProperty("Service/RemainAfterExit", UnitPropertyType.Bool, false)]
        public bool RemainAfterExit { get; set; }

        [UnitProperty("Service/GuessMainPID", UnitPropertyType.Bool, false)]
        public bool GuessMainPID { get; set; }

        [UnitProperty("Service/PIDFile", UnitPropertyType.String)]
        public string PIDFile { get; set; }

        [UnitProperty("Service/BusName", UnitPropertyType.String)]
        public string BusName { get; set; }

        #region Restart behavior
        [UnitProperty("Service/Restart", UnitPropertyType.Enum, RestartBehavior.No, typeof(RestartBehavior))]
        public RestartBehavior Restart { get; set; }

        [UnitProperty("Service/RestartSec", UnitPropertyType.Time, "100ms")]
        public TimeSpan RestartSec { get; set; }

        #endregion

        #region Exec= command lines
        [UnitProperty("Service/ExecStartPre", UnitPropertyType.StringList)]
        public List<string> ExecStartPre { get; set; }

        [UnitProperty("Service/ExecStart", UnitPropertyType.StringList)]
        public List<string> ExecStart { get; set; }

        [UnitProperty("Service/ExecStartPost", UnitPropertyType.StringList)]
        public List<string> ExecStartPost { get; set; }

        [UnitProperty("Service/ExecStop", UnitPropertyType.StringList)]
        public List<string> ExecStop { get; set; }

        [UnitProperty("Service/ExecReload", UnitPropertyType.StringList)]
        public List<string> ExecReload { get; set; }
        #endregion

        public ServiceUnitDescriptor() { }
    }

    public enum RestartBehavior
    {
        No,
        Always,
        OnSuccess,
        OnFailure,
        OnAbnormal,
        OnAbort,
        OnWatchdog
    }

    public enum ServiceType
    {
        Simple, // only one supported so far
        Exec,
        Forking,
        Oneshot,
        Dbus,
        Notify,
        Idle
    }
}
