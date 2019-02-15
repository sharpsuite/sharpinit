using NLog;
using SharpInit.Ipc;
using SharpInit.Units;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SharpInit
{
    class ServerIpcContext : IBaseIpcContext
    {
        Logger Log = LogManager.GetCurrentClassLogger();

        public ServerIpcContext()
        {

        }

        public bool ActivateUnit(string name)
        {
            var transaction = UnitRegistry.CreateActivationTransaction(name);
            var result = transaction.Execute();
            return result.Type == Tasks.ResultType.Success;
        }

        public bool DeactivateUnit(string name)
        {
            var transaction = UnitRegistry.CreateDeactivationTransaction(name);
            var result = transaction.Execute();
            return result.Type == Tasks.ResultType.Success;
        }

        public bool ReloadUnit(string name)
        {
            var transaction = UnitRegistry.GetUnit(name).GetReloadTransaction();
            var result = transaction.Execute();
            return result.Type == Tasks.ResultType.Success;
        }

        public List<string> ListUnits()
        {
            return UnitRegistry.Units.Select(u => u.Key).ToList();
        }

        public bool LoadUnitFromFile(string path)
        {
            try
            {
                UnitRegistry.AddUnitByPath(path);
                return true;
            }
            catch { return false; }
        }

        public bool ReloadUnitFile(string unit)
        {
            UnitRegistry.GetUnit(unit).ReloadUnitFile();
            return true;
        }

        public UnitInfo GetUnitInfo(string unit_name)
        {
            var unit = UnitRegistry.GetUnit(unit_name);
            var info = new UnitInfo();

            info.Name = unit.UnitName;
            info.Path = unit.File.UnitPath;
            info.Description = unit.File.Description;
            info.State = Enum.Parse<Ipc.UnitState>(unit.CurrentState.ToString());
            info.LastStateChangeTime = unit.LastStateChangeTime;
            info.ActivationTime = unit.ActivationTime;
            info.LoadTime = unit.LoadTime;

            return info;
        }
    }
}
