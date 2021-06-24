using SharpInit.Tasks;
using SharpInit.Platform;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SharpInit.Units
{
    public static class UnitRegistry
    {
        public static event OnUnitStateChange UnitStateChange;

        public static Logger Log = LogManager.GetCurrentClassLogger();

        public static Dictionary<string, List<UnitFile>> UnitFiles = new Dictionary<string, List<UnitFile>>();

        public static Dictionary<string, Unit> Units = new Dictionary<string, Unit>();
        public static Dictionary<string, Type> UnitTypes = new Dictionary<string, Type>();
        public static Dictionary<Type, Type> UnitDescriptorTypes = new Dictionary<Type, Type>();

        public static DependencyGraph<OrderingDependency> OrderingDependencies = new DependencyGraph<OrderingDependency>();
        public static DependencyGraph<RequirementDependency> RequirementDependencies = new DependencyGraph<RequirementDependency>();

        public static ServiceManager ServiceManager = new ServiceManager();

        public static SocketManager SocketManager = new SocketManager();

        public static ISymlinkTools SymlinkTools { get; set; }

        public static object GlobalTransactionLock = new object();

        public static List<string> DefaultScanDirectories = new List<string>()
        {
            "./units",
            "/etc/sharpinit/units",
            "/usr/local/sharpinit/units"
        };

        public static List<string> ScanDirectories = new List<string>();

        public static void CreateBaseUnits()
        {
            var default_target_file = new GeneratedUnitFile("default.target")
                .WithProperty("Unit/Description", "default.target");

            IndexUnitFile(default_target_file);

            var sockets_target_file = new GeneratedUnitFile("sockets.target")
                .WithProperty("Unit/Description", "sockets.target");
            
            IndexUnitFile(sockets_target_file);
        }

        public static void AddUnit(Unit unit)
        {
            if (unit == null)
                return;

            if (Units.ContainsKey(unit.UnitName))
                throw new InvalidOperationException();

            unit.ServiceManager = ServiceManager;
            unit.SocketManager = SocketManager;
            unit.UnitStateChange += PropagateStateChange;
            unit.RegisterDependencies(OrderingDependencies, RequirementDependencies);
            Units[unit.UnitName] = unit;
        }

        private static void PropagateStateChange(object sender, UnitStateChangeEventArgs e)
        {
            UnitStateChange?.Invoke(sender, e);
        }

        public static string GetUnitName(string path)
        {
            path = path.Replace('\\', '/');

            var filename = Path.GetFileName(path);
            var filename_without_ext = Path.GetFileNameWithoutExtension(path);

            if (filename_without_ext.Contains("@"))
                return filename_without_ext.Split('@').First() + "@" + Path.GetExtension(filename);

            return filename;
        }

        public static string GetUnitParameter(string path)
        {
            var filename = Path.GetFileName(path);
            var filename_without_ext = Path.GetFileNameWithoutExtension(path);

            if (filename_without_ext.Contains("@"))
                return string.Join('@', filename_without_ext.Split('@').Skip(1));

            return "";
        }

        public static int ScanDefaultDirectories()
        {
            int count = 0;

            OrderingDependencies.Dependencies.Clear();
            RequirementDependencies.Dependencies.Clear();

            var env_units_parts = (Environment.GetEnvironmentVariable("SHARPINIT_UNIT_PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries);

            ScanDirectories.Clear();
            ScanDirectories.AddRange(DefaultScanDirectories);
            ScanDirectories.AddRange(env_units_parts.Where(Directory.Exists));

            foreach (var unit in Units)
            {
                unit.Value.ReloadUnitDescriptor();
                unit.Value.RegisterDependencies(OrderingDependencies, RequirementDependencies);
            }

            foreach (var dir in ScanDirectories)
            {
                if (!Directory.Exists(dir))
                    continue;

                count += ScanDirectory(dir);
            }

            return count;
        }

        public static int ScanDirectory(string path, bool recursive = true)
        {
            var directories = recursive ? Directory.GetDirectories(path) : new string[0];
            var files = Directory.GetFiles(path);

            int count = 0;

            foreach (var file in files)
            {
                if (!UnitTypes.Any(type => file.EndsWith(type.Key))) 
                {
                    continue;
                }
                
                if (SymlinkTools.IsSymlink(file))
                {
                    var target = SymlinkTools.GetTarget(file);

                    Log.Warn($"Symlink detected from {file} to {target}");

                    var fileinfo = new FileInfo(file);

                    // If symlinked to an empty file, disable the unit.
                    if (fileinfo.Length == 0)
                    {
                        IndexUnitFile(new GeneratedUnitFile(GetUnitName(file)).WithProperty("Disabled", "yes"));
                        continue;
                    }

                    // Check if this unit file has already been indexed or not.
                    if (!UnitFiles.Any(unit_files => unit_files.Value.OfType<OnDiskUnitFile>().Any(unit_file => unit_file.Path == target)))
                    {
                        // If the file hasn't been indexed yet, do so. This check prevent symlinked files from being parsed more than once.
                        IndexUnitByPath(target);
                    }

                    // detect .wants, .requires
                    var directory_maps = new Dictionary<string, string>()
                    {
                        {".wants", "Unit/Wants" },
                        {".requires", "Unit/Requires" },
                    };

                    var directory_name = Path.GetDirectoryName(file);

                    foreach (var directory_mapping in directory_maps) 
                    {
                        if (directory_name.EndsWith(directory_mapping.Key))
                        {
                            // If we find a directory mapping (like default.target.wants/sshd.service), create an in-memory unit file to
                            // store the mapped property (in this example, it would be a unit file for default.target that contains
                            // Wants=sshd.service.
                            var unit_name = Path.GetFileName(directory_name);
                            unit_name = unit_name.Substring(0, unit_name.Length - directory_mapping.Key.Length);
                            var temp_unit_file = new GeneratedUnitFile(unit_name).WithProperty(directory_mapping.Value, GetUnitName(file));
                            IndexUnitFile(temp_unit_file);
                        }
                    }
                }
                else
                {
                    IndexUnitByPath(file);
                }

                count++;
            }

            foreach (var dir in directories)
                count += ScanDirectory(dir, recursive);

            return count;
        }

        public static Unit GetUnit(string name) => GetUnit<Unit>(name);

        public static T GetUnit<T>(string name) where T : Unit
        {
            if (Units.ContainsKey(name))
            {
                return Units[name] as T;
            }
            
            var new_unit = CreateUnit(name);
            if (new_unit != null)
            {
                AddUnit(new_unit);
                return new_unit as T;
            }

            return null;
        }

        public static bool IndexUnitFile(UnitFile file)
        {
            var name = file.UnitName;

            if (!UnitFiles.ContainsKey(name))
                UnitFiles[name] = new List<UnitFile>();

            if (file is OnDiskUnitFile)
                UnitFiles[name].RemoveAll(u => 
                u is OnDiskUnitFile && 
                (u as OnDiskUnitFile).Path == (file as OnDiskUnitFile).Path);

            UnitFiles[name].Add(file);

            if (Units.ContainsKey(name))
            {
                var unit = Units[name];
                unit.SetUnitDescriptor(GetUnitDescriptor(name));
                unit.RegisterDependencies(OrderingDependencies, RequirementDependencies);
            }
            else
            {
                AddUnit(CreateUnit(name));
            }

            return true;
        }

        public static bool IndexUnitByPath(string path)
        {
            path = Path.GetFullPath(path);

            var unit_file = UnitParser.ParseFile(path);

            if (unit_file == null)
                return false;
            return IndexUnitFile(unit_file);
        }

        public static Unit CreateUnit(string name)
        {
            var pure_unit_name = GetUnitName(name);

            if (!UnitFiles.ContainsKey(pure_unit_name))
                return null;

            var files = UnitFiles[pure_unit_name];
            var ext = Path.GetExtension(name);

            if (!UnitTypes.ContainsKey(ext))
                return null;

            var type = UnitTypes[ext];
            var descriptor = GetUnitDescriptor(pure_unit_name);
            var context = new UnitInstantiationContext();

            context.Substitutions["p"] = pure_unit_name;
            context.Substitutions["P"] = StringEscaper.Unescape(pure_unit_name);
            context.Substitutions["f"] = "/" + StringEscaper.Unescape(pure_unit_name);
            context.Substitutions["H"] = Environment.MachineName;

            var unit_parameter = GetUnitParameter(name);

            if (string.IsNullOrEmpty(unit_parameter))
            {
                unit_parameter = descriptor.DefaultInstance;
            }

            if (!string.IsNullOrEmpty(unit_parameter))
            {
                context.Substitutions["i"] = unit_parameter;
                context.Substitutions["I"] = StringEscaper.Unescape(unit_parameter);
                context.Substitutions["f"] = "/" + StringEscaper.Unescape(unit_parameter);
            }

            descriptor.InstantiateDescriptor(context);
            return (Unit)Activator.CreateInstance(type, name, descriptor);
        }

        public static UnitDescriptor GetUnitDescriptor(string name)
        {
            var pure_unit_name = GetUnitName(name);
            var ext = Path.GetExtension(pure_unit_name);

            var type = UnitTypes[ext];

            var files = UnitFiles[pure_unit_name];
            return UnitParser.FromFiles(UnitDescriptorTypes[type], files.ToArray());
        }

        public static void InitializeTypes()
        {
            UnitTypes[".unit"] = typeof(Unit);
            UnitTypes[".service"] = typeof(ServiceUnit);
            UnitTypes[".target"] = typeof(TargetUnit);
            UnitTypes[".socket"] = typeof(SocketUnit);
            UnitTypes[".mount"] = typeof(MountUnit);

            UnitDescriptorTypes[typeof(Unit)] = typeof(UnitDescriptor);
            UnitDescriptorTypes[typeof(ServiceUnit)] = typeof(ServiceUnitDescriptor);
            UnitDescriptorTypes[typeof(TargetUnit)] = typeof(UnitDescriptor);
            UnitDescriptorTypes[typeof(SocketUnit)] = typeof(SocketUnitDescriptor);
            UnitDescriptorTypes[typeof(MountUnit)] = typeof(MountUnitDescriptor);

            SymlinkTools = PlatformUtilities.GetImplementation<ISymlinkTools>();
        }

        public static UnitStateChangeTransaction CreateActivationTransaction(string name, string reason = null) => CreateActivationTransaction(GetUnit(name), reason);
        

        public static UnitStateChangeTransaction CreateActivationTransaction(Unit unit, string reason = null)
        {
            var transaction = new UnitStateChangeTransaction(unit) { Name = $"Activate {unit.UnitName}", Lock = GlobalTransactionLock };

            if (reason != null)
                transaction.Add(new AlterTransactionContextTask("state_change_reason", reason));

            var unit_list = new List<Unit>() { unit };

            var ignore_conflict_deactivation_failure = new Dictionary<string, bool>();
            var fail_if_unstarted = new Dictionary<string, bool>();
            var ignore_failure = new Dictionary<string, bool>() { { unit.UnitName, false } };
            var req_graph = RequirementDependencies.TraverseDependencyGraph(unit.UnitName, t => t.RequirementType != RequirementDependencyType.Conflicts && t.RequirementType != RequirementDependencyType.PartOf, false);
            
            // list all units to be started
            foreach(var dependency in req_graph)
            {
                var parent = dependency.LeftUnit;
                var child = dependency.RightUnit;

                var target_unit = GetUnit(child);

                if (target_unit == null)
                    continue;

                if (!unit_list.Contains(target_unit))
                    unit_list.Add(target_unit);
                
                if (!transaction.Reasoning.ContainsKey(target_unit))
                {
                    transaction.Reasoning[target_unit] = new List<string>();
                }

                transaction.Reasoning[target_unit].Add($"Activating {target_unit.UnitName} because of dependency {dependency}");
            }

            // determine whether the failure of each unit activation makes the entire transaction fail
            string current_unit = unit.UnitName;
            var list = new List<RequirementDependency>();

            while (true)
            {
                var dependencies_to_resolve = req_graph.Where(dep => dep.LeftUnit == current_unit).ToList();

                foreach (var dependency in dependencies_to_resolve)
                {
                    if(dependency.RequirementType == RequirementDependencyType.Wants)
                    {
                        ignore_failure[dependency.RightUnit] = ignore_failure.ContainsKey(dependency.RightUnit) ? ignore_failure[dependency.RightUnit] : true;
                    }
                    else
                    {
                        ignore_failure[dependency.RightUnit] = false;
                        list.Add(dependency);
                    }
                }

                if (!list.Any())
                    break;

                current_unit = list.First().RightUnit;
                list.RemoveAt(0);
            }

            // determine whether each unit is actually to be started or not (Requisite only checks whether the unit is active)
            fail_if_unstarted = unit_list.ToDictionary(u => u.UnitName, u => RequirementDependencies.GetDependencies(u.UnitName).All(dep => dep.RequirementType == RequirementDependencyType.Requisite));
            fail_if_unstarted[unit.UnitName] = false; // the unit we're set out to start isn't subject to this

            // create unit ordering according to ordering dependencies
            var order_graph = OrderingDependencies.TraverseDependencyGraph(unit.UnitName, t => true, true).ToList();
            
            var new_order = new List<Unit>();
            var initial_nodes = order_graph.Where(dependency => !order_graph.Any(d => dependency.LeftUnit == d.RightUnit));
            var initial_nodes_filtered = initial_nodes.Where(dependency => unit_list.Any(u => dependency.LeftUnit == u.UnitName || dependency.RightUnit == u.UnitName));
            var selected_nodes = initial_nodes_filtered.Select(t => t.LeftUnit).Distinct().ToList(); // find the "first" nodes

            if (!initial_nodes_filtered.Any() && !initial_nodes.Any() && order_graph.Any())
            {
                // possible dependency loop
                throw new Exception($"Failed to order dependent units while preparing the activation transaction for {unit.UnitName}.");
            }
            else if (!initial_nodes_filtered.Any())
                new_order = unit_list;
            else
            {
                var processed_vertices = new List<string>();

                while(selected_nodes.Any())
                {
                    var dep = selected_nodes.First();
                    selected_nodes.Remove(dep);

                    var dep_unit = GetUnit(dep);

                    if (dep_unit == null)
                    {
                        if (!ignore_failure.ContainsKey(dep) || ignore_failure[dep])
                            continue;
                        else
                            throw new Exception($"Couldn't find required unit {dep}");
                    }
                    
                    new_order.Add(GetUnit(dep));

                    var other_edges = order_graph.Where(d => d.LeftUnit == dep).ToList();
                    other_edges.ForEach(edge => order_graph.Remove(edge));
                    var edges_to_add = other_edges.Where(edge => { var m = edge.RightUnit; return !order_graph.Any(e => e.RightUnit == m && !processed_vertices.Contains(e.LeftUnit)); }).Select(t => t.RightUnit);

                    selected_nodes.AddRange(edges_to_add);
                }

                new_order.Reverse();
                new_order = new_order.Concat(unit_list.Where(u => !new_order.Contains(u)).ToList()).ToList();

                // check the new order against the rules
                bool satisfied = true;

                foreach (var order_rule in order_graph)
                {
                    var index_1 = new_order.FindIndex(u => u.UnitName == order_rule.LeftUnit);
                    var index_2 = new_order.FindIndex(u => u.UnitName == order_rule.RightUnit);

                    if (index_1 < index_2)
                    {
                        if (UnitRegistry.GetUnit(order_rule.LeftUnit) == null)
                        {
                            Log.Warn($"Ignoring ordering dependency against missing unit {order_rule.LeftUnit}");
                            continue;
                        }

                        satisfied = false;
                        break;
                    }
                }

                if (!satisfied)
                    throw new Exception($"Unsatisfiable set of ordering rules encountered when building the activation transaction for unit {unit.UnitName}.");
            }

            unit_list = new_order;

            // get a list of units to stop
            var conflicts = unit_list.SelectMany(u => RequirementDependencies.GetDependencies(u.UnitName).Where(d => d.RequirementType == RequirementDependencyType.Conflicts));
            var units_to_stop = new List<Unit>();

            foreach(var conflicting_dep in conflicts)
            {
                var left = GetUnit(conflicting_dep.LeftUnit);
                var right = GetUnit(conflicting_dep.RightUnit);

                Unit unit_to_stop = null;

                if (unit_list.Contains(left) && unit_list.Contains(right)) // conflict inside transaction
                {
                    var left_ignorable = !ignore_failure.ContainsKey(left.UnitName) || ignore_failure[left.UnitName];
                    var right_ignorable = !ignore_failure.ContainsKey(right.UnitName) || ignore_failure[right.UnitName];

                    if (!left_ignorable && !right_ignorable)
                        throw new Exception($"Units {left.UnitName} and {right.UnitName} conflict because of dependency {conflicting_dep}");

                    if (left_ignorable && !units_to_stop.Contains(left))
                    {
                        unit_to_stop = left;
                    }

                    if (right_ignorable && !units_to_stop.Contains(right))
                    {
                        unit_to_stop = right;
                    }
                }
                else if (unit_list.Contains(left) && !units_to_stop.Contains(right))
                {
                    unit_to_stop = right;
                }
                else if (unit_list.Contains(right) && !units_to_stop.Contains(left))
                {
                    unit_to_stop = left;
                }
                else
                {
                    continue;
                }

                if (unit_to_stop == null)
                    continue;

                units_to_stop.Add(unit_to_stop);

                if (!transaction.Reasoning.ContainsKey(unit_to_stop))
                    transaction.Reasoning[unit_to_stop] = new List<string>();

                transaction.Reasoning[unit_to_stop].Add($"Deactivating {unit_to_stop.UnitName} because of dependency {conflicting_dep}");
            }

            ignore_conflict_deactivation_failure = units_to_stop.ToDictionary(u => u.UnitName,
                u => conflicts.All(conflict =>
                {
                    var requesting_end = u.UnitName == conflict.LeftUnit ? conflict.RightUnit : conflict.LeftUnit;
                    return !ignore_failure.ContainsKey(requesting_end) || ignore_failure[requesting_end];
                }));

            // actually create the transaction

            foreach (var sub_unit in units_to_stop)
            {
                var deactivation_transaction = CreateDeactivationTransaction(sub_unit, $"{unit.UnitName} is being activated");

                deactivation_transaction.Prepend(new CheckUnitStateTask(UnitState.Active, sub_unit.UnitName, true));
                deactivation_transaction.ErrorHandlingMode = ignore_conflict_deactivation_failure[sub_unit.UnitName] ? TransactionErrorHandlingMode.Ignore : TransactionErrorHandlingMode.Fail;

                transaction.Add(deactivation_transaction);
            }

            foreach(var sub_unit in unit_list)
            {
                var activation_transaction = sub_unit.GetActivationTransaction();

                if (fail_if_unstarted.ContainsKey(sub_unit.UnitName) && fail_if_unstarted[sub_unit.UnitName])
                    activation_transaction.Prepend(new CheckUnitStateTask(UnitState.Active, sub_unit.UnitName));

                if (ignore_failure.ContainsKey(sub_unit.UnitName) && !ignore_failure[sub_unit.UnitName])
                    activation_transaction.ErrorHandlingMode = TransactionErrorHandlingMode.Fail;
                else
                    activation_transaction.ErrorHandlingMode = TransactionErrorHandlingMode.Ignore;

                transaction.Add(activation_transaction);
            }

            return transaction;
        }

        public static UnitStateChangeTransaction CreateDeactivationTransaction(string unit, string reason = null) => CreateDeactivationTransaction(GetUnit(unit), reason);
        
        public static UnitStateChangeTransaction CreateDeactivationTransaction(Unit unit, string reason = null)
        {
            var transaction = new UnitStateChangeTransaction(unit) { Name = $"Deactivate {unit.UnitName}", Lock = GlobalTransactionLock };

            if (reason != null)
                transaction.Add(new AlterTransactionContextTask("state_change_reason", reason));

            var units_to_deactivate = RequirementDependencies.TraverseDependencyGraph(unit.UnitName, 
                t => t.RequirementType == RequirementDependencyType.BindsTo || 
                t.RequirementType == RequirementDependencyType.PartOf).SelectMany(dep => 
                {
                    var ret = new[] { dep.LeftUnit, dep.RightUnit };

                    if(dep.RequirementType == RequirementDependencyType.PartOf) // PartOf is a one way dependency
                    {
                        if (unit.UnitName != dep.RightUnit) // only when stopping the "right hand" side of a PartOf dependency should the action be propagated
                            return new string[0];
                    }

                    foreach (var unit_name in ret)
                    {
                        var u = GetUnit(unit_name);

                        if (u == null)
                            continue;

                        if (!transaction.Reasoning.ContainsKey(u))
                        {
                            transaction.Reasoning[u] = new List<string>();
                        }

                        transaction.Reasoning[u].Add($"Deactivating {u.UnitName} because of dependency {dep}");
                    }

                    return ret;
                })
                .Select(GetUnit).ToList();

            units_to_deactivate.Add(unit);
            units_to_deactivate = units_to_deactivate.Distinct().ToList();

            var order_graph = OrderingDependencies.TraverseDependencyGraph(unit.UnitName, t => units_to_deactivate.Any(u => u.UnitName == t.LeftUnit || u.UnitName == t.RightUnit), true).ToList();

            var new_order = new List<Unit>();
            var initial_nodes = order_graph.Where(dependency => !order_graph.Any(d => dependency.LeftUnit == d.RightUnit)).Select(t => t.LeftUnit).Distinct().ToList(); // find the "first" nodes

            if (!initial_nodes.Any() && order_graph.Any())
            {
                // possible dependency loop
                throw new Exception($"Failed to order dependent units while preparing the deactivation transaction for {unit.UnitName}.");
            }
            else if (!initial_nodes.Any())
            {
                new_order = units_to_deactivate;
            }
            else
            {
                var processed_vertices = new List<string>();

                while (initial_nodes.Any())
                {
                    var dep = initial_nodes.First();
                    initial_nodes.Remove(dep);
                    new_order.Add(GetUnit(dep));

                    var other_edges = order_graph.Where(d => d.LeftUnit == dep).ToList();
                    other_edges.ForEach(edge => order_graph.Remove(edge));
                    var edges_to_add = other_edges.Where(edge => { var m = edge.RightUnit; return !order_graph.Any(e => e.RightUnit == m && !processed_vertices.Contains(e.LeftUnit)); }).Select(t => t.RightUnit);

                    initial_nodes.AddRange(edges_to_add);
                }

                // prune the new order down to only units we've decided to deactivate, then append the unordered units not included in the new order
                new_order = new_order.Where(units_to_deactivate.Contains).Concat(units_to_deactivate.Where(u => !new_order.Contains(u)).ToList()).ToList();

                // check the new order against the rules
                bool satisfied = true;

                foreach (var order_rule in order_graph)
                {
                    var index_1 = new_order.FindIndex(u => u.UnitName == order_rule.LeftUnit);
                    var index_2 = new_order.FindIndex(u => u.UnitName == order_rule.RightUnit);

                    if (index_1 == -1 || index_2 == -1) // one of the vertices got pruned
                        continue;

                    if (index_1 > index_2) // now in reverse
                    {
                        satisfied = false;
                        break;
                    }
                }

                if (!satisfied)
                    throw new Exception($"Unsatisfiable set of ordering rules encountered when building the deactivation transaction for unit {unit.UnitName}.");
            }

            units_to_deactivate = new_order;

            // build the transaction

            foreach(var sub_unit in units_to_deactivate)
            {
                transaction.AffectedUnits.Add(sub_unit);
                transaction.Add(sub_unit.GetDeactivationTransaction());
            }

            return transaction;
        }
    }
}
