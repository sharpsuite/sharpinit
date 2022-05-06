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
        
        [UnitProperty("@/@", UnitPropertyType.Enum, Units.NotifyAccess.None, typeof(NotifyAccess))]
        public NotifyAccess NotifyAccess { get; set; }
        [UnitProperty("@/@", UnitPropertyType.Enum, ExitType.Main, typeof(ExitType))]
        public ExitType ExitType { get; set; }
        
        [UnitProperty("@/@", UnitPropertyType.Bool, default_value: false)]
        public bool Delegate { get; set; }

        #region Restart behavior
        [UnitProperty("Service/Restart", UnitPropertyType.Enum, RestartBehavior.No, typeof(RestartBehavior))]
        public RestartBehavior Restart { get; set; }

        [UnitProperty("Service/RestartSec", UnitPropertyType.Time, "100ms")]
        public TimeSpan RestartSec { get; set; }

        #endregion

        #region Exec= command lines
        [UnitProperty("Service/ExecCondition", UnitPropertyType.StringList)]
        public List<string> ExecCondition { get; set; }

        [UnitProperty("Service/ExecStartPre", UnitPropertyType.StringList)]
        public List<string> ExecStartPre { get; set; }

        [UnitProperty("Service/ExecStart", UnitPropertyType.StringList)]
        public List<string> ExecStart { get; set; }

        [UnitProperty("Service/ExecStartPost", UnitPropertyType.StringList)]
        public List<string> ExecStartPost { get; set; }

        [UnitProperty("Service/ExecStop", UnitPropertyType.StringList)]
        public List<string> ExecStop { get; set; }

        [UnitProperty("Service/ExecStopPost", UnitPropertyType.StringList)]
        public List<string> ExecStopPost { get; set; }

        [UnitProperty("Service/ExecReload", UnitPropertyType.StringList)]
        public List<string> ExecReload { get; set; }
        #endregion

        #region Timeouts
        [UnitProperty("Service/@", UnitPropertyType.Time, "5")]
        public TimeSpan TimeoutStartSec { get; set; }

        [UnitProperty("Service/@", UnitPropertyType.Time, "5")]
        public TimeSpan TimeoutStopSec { get; set; }
        #endregion

        public ServiceUnitDescriptor() : base() { }
    }

    public enum RestartBehavior
    {
        CleanExit = 0x1,
        UncleanExit = 0x2,
        UncleanSignal = 0x4,
        Timeout = 0x8,
        Watchdog = 0xF,

        No = 0,
        Always = CleanExit | UncleanExit | UncleanSignal | Timeout | Watchdog,
        OnSuccess = CleanExit,
        OnFailure = UncleanExit | UncleanSignal | Timeout | Watchdog,
        OnAbnormal = UncleanSignal | Timeout | Watchdog,
        OnAbort = UncleanSignal,
        OnWatchdog = Watchdog
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

    public enum ExitType
    {
        Main,
        CGroup
    }

    public enum NotifyAccess
    {
        None,
        Main,
        Exec,
        All
    }
}
