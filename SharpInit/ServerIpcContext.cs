using NLog;
using SharpInit.Ipc;
using SharpInit.Tasks;
using SharpInit.Units;
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;

namespace SharpInit
{
    class ServerIpcContext : IBaseIpcContext
    {
        Logger Log = LogManager.GetCurrentClassLogger();
        ServiceManager ServiceManager { get; set; }

        public ServerIpcContext(ServiceManager manager)
        {
            ServiceManager = manager;
        }

        public bool ActivateUnit(string name)
        {
            var transaction = LateBoundUnitActivationTask.CreateActivationTransaction(name, "Remotely triggered via IPC");
            var exec = ServiceManager.Runner.Register(transaction).Enqueue().Wait();
            var result = exec.Result;

            if (result.Type != ResultType.Success)
            {
                Log.Info($"Activation transaction failed. Result type: {result.Type}, message: {result.Message}");
                Log.Info("Transaction failed at highlighted task: ");

                var tree = transaction.GeneratedTransaction?.GenerateTree(0, result.Task) ?? "(failed to generate late-bound tx)";

                foreach (var line in tree.Split('\n'))
                    Log.Info(line);
            }

            return result.Type == ResultType.Success;
        }

        public bool DeactivateUnit(string name)
        {
            var transaction = LateBoundUnitActivationTask.CreateDeactivationTransaction(name, "Remotely triggered via IPC");
            var exec = ServiceManager.Runner.Register(transaction).Enqueue().Wait();
            var result = exec.Result;

            if (result.Type != ResultType.Success)
            {
                Log.Info($"Deactivation transaction failed. Result type: {result.Type}, message: {result.Message}");
                Log.Info("Transaction failed at highlighted task: ");

                var tree = transaction.GeneratedTransaction?.GenerateTree(0, result.Task) ?? "(failed to generate late-bound tx)";

                foreach (var line in tree.Split('\n'))
                    Log.Info(line);
            }

            return result.Type == ResultType.Success;
        }

        public Dictionary<string, List<string>> GetActivationPlan(string unit)
        {
            var transaction = ServiceManager.Planner.CreateActivationTransaction(unit);
            return transaction.Reasoning.ToDictionary(t => t.Key.UnitName, t => t.Value);
        }

        public Dictionary<string, List<string>> GetDeactivationPlan(string unit)
        {
            var transaction = ServiceManager.Planner.CreateDeactivationTransaction(unit);
            return transaction.Reasoning.ToDictionary(t => t.Key.UnitName, t => t.Value);
        }

        public bool ReloadUnit(string name)
        {
            var transaction = ServiceManager.Registry.GetUnit(name).GetReloadTransaction();
            var exec = ServiceManager.Runner.Register(transaction).Enqueue().Wait();
            var result = exec.Result;
            return result.Type == Tasks.ResultType.Success;
        }

        public List<string> ListUnits()
        {
            return ServiceManager.Registry.Units.Select(u => u.Key).ToList();
        }

        public List<string> ListUnitFiles()
        {
            return ServiceManager.Registry.UnitFiles.Select(p => string.Join(", ", p.Value.Select(t => t.ToString()))).ToList();
        }

        public bool LoadUnitFromFile(string path)
        {
            try
            {
                ServiceManager.Registry.IndexUnitByPath(path);
                return true;
            }
            catch { return false; }
        }

        public bool ReloadUnitFile(string unit)
        {
            return false;
        }

        public int RescanUnits()
        {
            return ServiceManager.Registry.ScanDefaultDirectories();
        }

        public UnitInfo GetUnitInfo(string unit_name)
        {
            var unit = ServiceManager.Registry.GetUnit(unit_name);
            var info = new UnitInfo();

            var unit_files = unit.Descriptor.Files;

            info.Name = unit.UnitName;
            info.Path = unit_files.Any() ? 
                string.Join(";", unit_files.Select(file => {
                    switch (file)
                    {
                        case OnDiskUnitFile disk_file:
                            return disk_file.Path;
                        default:
                            return file.ToString();
                    }
                })) :
                "(not available)";
            info.Documentation = unit.Descriptor.Documentation.ToArray();
            info.Description = unit.Descriptor.Description;
            info.CurrentState = Enum.Parse<Ipc.UnitState>(unit.CurrentState.ToString());
            info.PreviousState = Enum.Parse<Ipc.UnitState>(unit.PreviousState.ToString());
            info.LastStateChangeTime = unit.LastStateChangeTime;
            info.ActivationTime = unit.ActivationTime;
            info.LoadTime = unit.Descriptor.Created;
            info.StateChangeReason = unit.StateChangeReason;
            info.MainProcessId = unit.MainProcessId;
            info.LogLines = GetJournal(unit_name, 10);

            if (unit.CGroup?.Exists == true)
                info.ProcessTree = unit.CGroup.Walk().ToList();
            else
                info.ProcessTree = new List<string>();

            return info;
        }

        public Dictionary<string, List<string>> GetUnitProperties(string name)
        {
            var unit = ServiceManager.Registry.GetUnit(name);

            if (unit == null)
                return null;
            
            return unit.Descriptor.GetProperties();
        }

        public List<string> GetJournal(string journal, int lines)
        {
            var entries = ServiceManager.Journal.Tail(journal, lines);

            if (!entries.Any())
                return new List<string>();

            var longest_source_length = entries.Max(e => e.Source.Length);
            var max_allowed_source_length = 30;

            if (longest_source_length >= max_allowed_source_length)
                longest_source_length = max_allowed_source_length;

            return entries.Select(entry => $"[{entry.LocalTime,12:0.000000}] [ {StringEscaper.Truncate(entry.Source, longest_source_length).PadLeft(longest_source_length)} ] {entry.Message}").ToList();
        }

        public bool InstallUnit(string unit_name)
        {
            var symlinks = Platform.PlatformUtilities.GetImplementation<Platform.ISymlinkTools>();
            var unit = ServiceManager.Registry.GetUnit(unit_name);
            var wanted_bys = unit.Descriptor.WantedBy;
            
            var unit_source_path = unit.Descriptor.Files.FirstOrDefault(f => Directory.Exists(Path.GetDirectoryName(f.Path))).Path ?? $"/etc/sharpinit/units/{unit_name}";
            var chief_dir = Path.GetDirectoryName(unit_source_path);

            foreach(var wanted_by in wanted_bys) 
            {
                var target_dir = $"{chief_dir}/{wanted_by}.wants";
                var target_file = $"{target_dir}/{unit_name}";

                if (!Directory.Exists(target_dir))
                    Directory.CreateDirectory(target_dir);
                
                if (File.Exists(target_file))
                {
                    Log.Warn($"Skipping enablement symlink {unit_source_path} => {target_file} because target file already exists");
                    continue;
                }

                if (!symlinks.CreateSymlink(unit_source_path, target_file, true)) 
                    Log.Warn($"Failed to enable symlink {unit_source_path} => {target_file}");
            }

            return true;
        }

        public bool UninstallUnit(string unit_name)
        {
            return false;
        }

        public bool MoveToCGroup(string cgroup_name)
        {
            if (ServiceManager.CGroupManager == null)
                return false;

            if (cgroup_name.StartsWith("0::"))
                cgroup_name = cgroup_name.Substring("0::".Length);
            
            ServiceManager.CGroupManager.UpdateRoot(ServiceManager.CGroupManager.GetCGroup(cgroup_name));
            ServiceManager.CGroupManager.MarkCGroupWritable(cgroup_name);
            
            ServiceManager.MoveToScope("init.scope");
            return true;
        }
        
        public int GetServiceManagerProcessId() => Environment.ProcessId;
    }
}
