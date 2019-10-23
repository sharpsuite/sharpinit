using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SharpInit.Units
{
    public class UnitDescriptor
    {
        public IEnumerable<UnitFile> Files { get; set; }

        #region Base properties
        [UnitProperty("Unit/Description")]
        public string Description { get; set; }

        [UnitProperty("Unit/SourcePath")]
        public string SourcePath { get; set; }

        [UnitProperty("Unit/Documentation", UnitPropertyType.StringListSpaceSeparated)]
        public List<string> Documentation { get; set; }
        #endregion

        #region Dependency properties
        [UnitProperty("Unit/Requires", UnitPropertyType.StringListSpaceSeparated)]
        public List<string> Requires { get; set; }

        [UnitProperty("Unit/Requisite", UnitPropertyType.StringListSpaceSeparated)]
        public List<string> Requisite { get; set; }
        
        [UnitProperty("Unit/Wants", UnitPropertyType.StringListSpaceSeparated)]
        public List<string> Wants { get; set; }
        
        [UnitProperty("Unit/BindsTo", UnitPropertyType.StringListSpaceSeparated)]
        public List<string> BindsTo { get; set; }

        [UnitProperty("Unit/PartOf", UnitPropertyType.StringListSpaceSeparated)]
        public List<string> PartOf { get; set; }

        [UnitProperty("Unit/Conflicts", UnitPropertyType.StringListSpaceSeparated)]
        public List<string> Conflicts { get; set; }

        [UnitProperty("Unit/Before", UnitPropertyType.StringListSpaceSeparated)]
        public List<string> Before { get; set; }

        [UnitProperty("Unit/After", UnitPropertyType.StringListSpaceSeparated)]
        public List<string> After { get; set; }
        
        [UnitProperty("Unit/PropagatesReloadTo", UnitPropertyType.StringListSpaceSeparated)]
        public List<string> PropagatesReloadTo { get; set; }
        
        [UnitProperty("Unit/ReloadPropagatedFrom", UnitPropertyType.StringListSpaceSeparated)]
        public List<string> ReloadPropagatedFrom { get; set; }
        
        [UnitProperty("Unit/JoinsNamespaceOf", UnitPropertyType.StringListSpaceSeparated)]
        public List<string> JoinsNamespaceOf { get; set; }
        
        [UnitProperty("Unit/RequiresMountsFor", UnitPropertyType.StringListSpaceSeparated)]
        public List<string> RequiresMountsFor { get; set; }
        
        [UnitProperty("Unit/DefaultDependencies", UnitPropertyType.Bool, true)]
        public bool DefaultDependencies { get; set; }
        #endregion

        #region Isolation properties
        [UnitProperty("Unit/AllowIsolate", UnitPropertyType.Bool, false)]
        public bool AllowIsolate { get; set; }

        [UnitProperty("Unit/IgnoreOnIsolate", UnitPropertyType.Bool)]
        public bool IgnoreOnIsolate { get; set; }
        #endregion

        #region Start/stop properties
        [UnitProperty("Unit/StopWhenUnneeded", UnitPropertyType.Bool, false)]
        public bool StopWhenUnneeded { get; set; }

        [UnitProperty("Unit/RefuseManualStart", UnitPropertyType.Bool, false)]
        public bool RefuseManualStart { get; set; }

        [UnitProperty("Unit/RefuseManualStop", UnitPropertyType.Bool, false)]
        public bool RefuseManualStop { get; set; }
        #endregion

        #region Failure, success, and collection properties
        [UnitProperty("Unit/OnFailureJobMode", UnitPropertyType.Enum, OnFailureJobMode.Replace, typeof(OnFailureJobMode))]
        public OnFailureJobMode OnFailureJobMode { get; set; }

        [UnitProperty("Unit/CollectMode", UnitPropertyType.Enum, CollectMode.Inactive, typeof(CollectMode))]
        public CollectMode CollectMode { get; set; }

        [UnitProperty("Unit/FailureAction", UnitPropertyType.Enum, FailureOrSuccessAction.None, typeof(FailureOrSuccessAction))]
        public FailureOrSuccessAction FailureAction { get; set; }

        [UnitProperty("Unit/SuccessAction", UnitPropertyType.Enum, FailureOrSuccessAction.None, typeof(FailureOrSuccessAction))]
        public FailureOrSuccessAction SuccessAction { get; set; }

        [UnitProperty("Unit/RebootArgument", UnitPropertyType.String)]
        public string RebootArgument { get; set; }

        [UnitProperty("Unit/FailureActionExitStatus", UnitPropertyType.Int, -1)]
        public int FailureActionExitStatus { get; set; }

        [UnitProperty("Unit/SuccessActionExitStatus", UnitPropertyType.Int, -1)]
        public int SuccessActionExitStatus { get; set; }

        #endregion

        #region Timeout properties
        [UnitProperty("Unit/JobTimeoutSec", UnitPropertyType.Int, -1)]
        public int JobTimeoutSec { get; set; }

        [UnitProperty("Unit/JobRunningTimeoutSec", UnitPropertyType.Int, -1)]
        public int JobRunningTimeoutSec { get; set; }

        [UnitProperty("Unit/JobTimeoutAction", UnitPropertyType.Enum, FailureOrSuccessAction.None, typeof(FailureOrSuccessAction))]
        public FailureOrSuccessAction JobTimeoutAction { get; set; }

        [UnitProperty("Unit/JobTimeoutRebootArgument", UnitPropertyType.String)]
        public string JobTimeoutRebootArgument { get; set; }
        #endregion

        #region Limiting properties
        [UnitProperty("Unit/StartLimitIntervalSec", UnitPropertyType.Int, -1)]
        public int StartLimitIntervalSec { get; set; }

        [UnitProperty("Unit/StartLimitBurst", UnitPropertyType.Int, -1)]
        public int StartLimitBurst { get; set; }

        [UnitProperty("Unit/StartLimitAction", UnitPropertyType.Enum, FailureOrSuccessAction.None, typeof(FailureOrSuccessAction))]
        public FailureOrSuccessAction StartLimitAction { get; set; }
        #endregion

        #region Condition/assertion properties
        // TODO: Use a better method for this
        // These currently map the vast amount of conditions/assertions supported by systemd into
        // dictionaries.
        //
        // Example: ConditionPathExists=!/usr/bin will be mapped to: 
        // Conditions["PathExists"] = { "!/usr/bin" } (shortened for brevity)
        public Dictionary<string, List<string>> Conditions = new Dictionary<string, List<string>>();
        public Dictionary<string, List<string>> Assertions = new Dictionary<string, List<string>>();
        #endregion
        
        public UnitDescriptor()
        {

        }
    }

    public enum OnFailureJobMode
    {
        Fail,
        Replace,
        ReplaceIrreversibly,
        Isolate,
        Flush,
        IgnoreDependencies,
        IgnoreRequirements
    }

    public enum CollectMode
    {
        Inactive,
        InactiveOrFailed
    }

    public enum FailureOrSuccessAction
    {
        None,
        Reboot,
        RebootForce,
        RebootImmediate,
        Poweroff,
        PoweroffForce,
        PoweroffImmediate,
        Exit,
        ExitForce
    }
}
