using NLog;
using SharpInit.Ipc;
using SharpInit.Tasks;
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

            if (result.Type != ResultType.Success)
            {
                Log.Info($"Activation transaction failed. Result type: {result.Type}, message: {result.Message}");
                Log.Info("Transaction failed at highlighted task: ");

                var tree = transaction.GenerateTree(0, result.Task);

                foreach (var line in tree.Split('\n'))
                    Log.Info(line);
            }

            return result.Type == ResultType.Success;
        }

        public bool DeactivateUnit(string name)
        {
            var transaction = UnitRegistry.CreateDeactivationTransaction(name);
            var result = transaction.Execute();

            if (result.Type != ResultType.Success)
            {
                Log.Info($"Deactivation transaction failed. Result type: {result.Type}, message: {result.Message}");
                Log.Info("Transaction failed at highlighted task: ");

                var tree = transaction.GenerateTree(0, result.Task);

                foreach (var line in tree.Split('\n'))
                    Log.Info(line);
            }

            return result.Type == ResultType.Success;
        }

        public Dictionary<string, List<string>> GetActivationPlan(string unit)
        {
            var transaction = UnitRegistry.CreateActivationTransaction(unit);
            return transaction.Reasoning.ToDictionary(t => t.Key.UnitName, t => t.Value);
        }

        public Dictionary<string, List<string>> GetDeactivationPlan(string unit)
        {
            var transaction = UnitRegistry.CreateDeactivationTransaction(unit);
            return transaction.Reasoning.ToDictionary(t => t.Key.UnitName, t => t.Value);
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

        public List<string> ListUnitFiles()
        {
            return UnitRegistry.UnitFiles.Select(p => string.Join(", ", p.Value.Select(t => t.ToString()))).ToList();
        }

        public bool LoadUnitFromFile(string path)
        {
            try
            {
                UnitRegistry.IndexUnitByPath(path);
                return true;
            }
            catch { return false; }
        }

        public bool ReloadUnitFile(string unit)
        {
            UnitRegistry.GetUnit(unit).ReloadUnitDescriptor();
            return true;
        }

        public int RescanUnits()
        {
            return UnitRegistry.ScanDefaultDirectories();
        }

        public UnitInfo GetUnitInfo(string unit_name)
        {
            var unit = UnitRegistry.GetUnit(unit_name);
            var info = new UnitInfo();

            var unit_files = unit.Descriptor.Files.OfType<OnDiskUnitFile>();

            info.Name = unit.UnitName;
            info.Path = unit_files.Any() ? 
                string.Join(", ", unit_files.Select(file => file.Path)) :
                "(not available)";
            info.Description = unit.Descriptor.Description;
            info.CurrentState = Enum.Parse<Ipc.UnitState>(unit.CurrentState.ToString());
            info.PreviousState = Enum.Parse<Ipc.UnitState>(unit.PreviousState.ToString());
            info.LastStateChangeTime = unit.LastStateChangeTime;
            info.ActivationTime = unit.ActivationTime;
            info.LoadTime = unit.Descriptor.Created;
            info.StateChangeReason = unit.StateChangeReason;
            info.LogLines = UnitRegistry.ServiceManager.Journal.Tail(unit_name, 10).Select(entry => entry.Message).ToList();

            return info;
        }

        public List<string> GetJournal(string journal, int lines)
        {
            return UnitRegistry.ServiceManager.Journal.Tail(journal, lines).Select(entry => $"[{entry.Source}] {entry.Message}").ToList();
        }
    }
}
