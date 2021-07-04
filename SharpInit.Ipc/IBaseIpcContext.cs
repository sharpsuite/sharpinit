using System;
using System.Collections.Generic;
using System.Text;

namespace SharpInit.Ipc
{
    /// <summary>
    /// This is an interface that essentially defines a contracted API between a SharpInit.Ipc client and server.
    /// A server must provide an implementation of this interface that performs work.
    /// For a client implementation that simply performs IPC calls, see ClientIpcContext.
    /// </summary>
    public interface IBaseIpcContext
    {
        [IpcFunction("activate-unit")]
        bool ActivateUnit(string name);

        [IpcFunction("deactivate-unit")]
        bool DeactivateUnit(string name);

        [IpcFunction("reload-unit")]
        bool ReloadUnit(string name);

        [IpcFunction("load-unit-file")]
        bool LoadUnitFromFile(string path);

        [IpcFunction("reload-unit-file")]
        bool ReloadUnitFile(string unit);

        [IpcFunction("rescan-units")]
        int RescanUnits();

        [IpcFunction("list-units")]
        List<string> ListUnits();

        [IpcFunction("list-unit-files")]
        List<string> ListUnitFiles();

        [IpcFunction("get-unit-info")]
        UnitInfo GetUnitInfo(string unit);

        [IpcFunction("get-activation-plan")]
        Dictionary<string, List<string>> GetActivationPlan(string unit);

        [IpcFunction("get-deactivation-plan")]
        Dictionary<string, List<string>> GetDeactivationPlan(string unit);

        [IpcFunction("get-journal")]
        List<string> GetJournal(string journal, int lines);

        [IpcFunction("install-unit")]
        bool InstallUnit(string name);
        [IpcFunction("install-unit")]
        bool UninstallUnit(string name);
        
    }
}
