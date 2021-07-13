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
    public class UnitRegistry
    {
        public static Dictionary<string, Type> UnitTypes = new Dictionary<string, Type>();
        public static Dictionary<Type, Type> UnitDescriptorTypes = new Dictionary<Type, Type>();
        
        public static List<string> DefaultScanDirectories = new List<string>()
        {
            "./units",
            "/etc/sharpinit/units",
            "/usr/local/sharpinit/units"
        };

        public event OnUnitAdded UnitAdded;

        public Logger Log = LogManager.GetCurrentClassLogger();

        public Dictionary<string, List<UnitFile>> UnitFiles = new Dictionary<string, List<UnitFile>>();

        public Dictionary<string, Unit> Units = new Dictionary<string, Unit>();
        public Dictionary<string, List<string>> Aliases = new Dictionary<string, List<string>>();

        public DependencyGraph<OrderingDependency> OrderingDependencies = new DependencyGraph<OrderingDependency>();
        public DependencyGraph<RequirementDependency> RequirementDependencies = new DependencyGraph<RequirementDependency>();

        public List<string> ScanDirectories = new List<string>();
        private ServiceManager ServiceManager { get; set; }

        private ISymlinkTools SymlinkTools => ServiceManager.SymlinkTools;

        internal UnitRegistry(ServiceManager manager)
        {
            ServiceManager = manager;
        }

        public bool AliasUnit(Unit unit, string alias)
        {
            if (Units.ContainsKey(alias))
                return false;
            
            if (!Units.ContainsValue(unit))
                return false;
            
            if (!Aliases.ContainsKey(alias))
                Aliases[alias] = new List<string>();
            
            Aliases[alias].Add(unit.UnitName);
            return true;
        }

        public void CreateBaseUnits()
        {
            var default_target_file = new GeneratedUnitFile("default.target")
                .WithProperty("Unit/Description", "default.target");

            IndexUnitFile(default_target_file);

            var sockets_target_file = new GeneratedUnitFile("sockets.target")
                .WithProperty("Unit/Description", "sockets.target");
            
            IndexUnitFile(sockets_target_file);
        }

        public void AddUnit(Unit unit)
        {
            if (unit == null)
                return;

            if (Units.ContainsKey(unit.UnitName))
                throw new InvalidOperationException();
            
            unit.ServiceManager = ServiceManager;

            unit.RegisterDependencies(OrderingDependencies, RequirementDependencies);
            Units[unit.UnitName] = unit;
            UnitAdded?.Invoke(this, new UnitAddedEventArgs(unit));
        }

        public int ScanDefaultDirectories()
        {
            int count = 0;
            var env_units_parts = (Environment.GetEnvironmentVariable("SHARPINIT_UNIT_PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries);

            ScanDirectories.Clear();
            ScanDirectories.AddRange(DefaultScanDirectories);
            ScanDirectories.AddRange(env_units_parts.Where(Directory.Exists));

            ReloadPre();

            foreach (var dir in ScanDirectories)
            {
                if (!Directory.Exists(dir))
                    continue;

                count += ScanDirectory(dir);
            }

            Reload();

            return count;
        }

        public void ReloadPre()
        {
            OrderingDependencies.Dependencies.Clear();
            RequirementDependencies.Dependencies.Clear();
            foreach (var unit_file_pair in UnitFiles)
            {
                unit_file_pair.Value.RemoveAll(file => { return (file is GeneratedUnitFile && (file as GeneratedUnitFile).DestroyOnReload); });
            }
        }

        public void Reload()
        {
            foreach (var unit in Units)
            {
                unit.Value.SetUnitDescriptor(GetUnitDescriptor(unit.Key));
                unit.Value.RegisterDependencies(OrderingDependencies, RequirementDependencies);
            }
        }

        public int ScanDirectory(string path, bool recursive = true)
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

                    Log.Debug($"Symlink detected from {file} to {target}");

                    var fileinfo = new FileInfo(file);

                    // If symlinked to an empty file, disable the unit.
                    if (fileinfo.Length == 0)
                    {
                        IndexUnitFile(new GeneratedUnitFile(UnitParser.GetUnitName(file), destroy_on_reload: true).WithProperty("Disabled", "yes"));
                        continue;
                    }

                    // Check if this unit file has already been indexed or not.
                    if (!UnitFiles.Any(unit_files => unit_files.Value.OfType<OnDiskUnitFile>().Any(unit_file => unit_file.Path == target)))
                    {
                        // If the file hasn't been indexed yet, do so. This check prevents symlinked files from being parsed more than once.
                        IndexUnitByPath(target);
                    }
                    else if (!Units.Any(u => u.Key != UnitParser.GetUnitName(file, with_parameter: true)))
                    {
                        // This branch handles symlinked and instantiated units. TODO: Check whether this is correct behavior.
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
                            var temp_unit_file = new GeneratedUnitFile(unit_name, destroy_on_reload: true).WithProperty(directory_mapping.Value, UnitParser.GetUnitName(file, with_parameter: true));
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

        public Unit GetUnit(string name) => GetUnit<Unit>(name);

        public T GetUnit<T>(string name) where T : Unit
        {
            if (Units.ContainsKey(name))
            {
                return Units[name] as T;
            }

            if (CheckForAlias(name) != name)
            {
                return Units[CheckForAlias(name)] as T;
            }
            
            var new_unit = CreateUnit(name);
            if (new_unit != null)
            {
                AddUnit(new_unit);
                return new_unit as T;
            }

            return null;
        }

        public bool IndexUnitFile(UnitFile file, bool create_unit = true)
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
            else if (create_unit)
            {
                AddUnit(CreateUnit(name));
            }

            return true;
        }

        public bool IndexUnitByPath(string path)
        {
            path = Path.GetFullPath(path);

            var unit_file = UnitParser.ParseFile(path);

            if (unit_file == null)
                return false;
            return IndexUnitFile(unit_file);
        }

        public Unit CreateUnit(string name)
        {
            var parametrized_unit_name = UnitParser.GetUnitName(name, with_parameter: true);
            var unparametrized_unit_name = UnitParser.GetUnitName(name, with_parameter: false);

            List<UnitFile> files = default;

            if (UnitFiles.ContainsKey(parametrized_unit_name))
                files = UnitFiles[parametrized_unit_name];
            else if (UnitFiles.ContainsKey(unparametrized_unit_name))
                files = UnitFiles[unparametrized_unit_name];
            else
            {
                if (name.EndsWith(".slice")) // Temporary, implement something like UnitGenerator to get around this
                {
                    IndexUnitFile(new GeneratedUnitFile(name, destroy_on_reload: false).WithProperty("Unit/Description", name), create_unit: false);
                    files = UnitFiles[unparametrized_unit_name];
                }
                else
                    return null;
            }

            var ext = Path.GetExtension(name);

            if (!UnitTypes.ContainsKey(ext))
                return null;

            var type = UnitTypes[ext];
            var descriptor = GetUnitDescriptor(name);
            return (Unit)Activator.CreateInstance(type, name, descriptor);
        }

        public string CheckForAlias(string name)
        {
            if (Aliases.ContainsKey(name))
            {
                return Aliases[name].FirstOrDefault(Units.ContainsKey);
            }

            return name;
        }

        public UnitDescriptor GetUnitDescriptor(string name)
        {
            string unit_name = name;
            var parametrized_unit_name = UnitParser.GetUnitName(name, with_parameter: true);
            var unparametrized_unit_name = UnitParser.GetUnitName(name, with_parameter: false);

            Type type;
            UnitFile[] files;

            lock (UnitFiles)
            {
                if (UnitFiles.ContainsKey(parametrized_unit_name))
                    unit_name = parametrized_unit_name;
                else if (UnitFiles.ContainsKey(unparametrized_unit_name))
                    unit_name = unparametrized_unit_name;
                
                if (!UnitFiles.ContainsKey(unit_name))
                {
                    if (UnitFiles.ContainsKey(CheckForAlias(parametrized_unit_name)))
                        unit_name = CheckForAlias(parametrized_unit_name);
                    else if (UnitFiles.ContainsKey(CheckForAlias(unparametrized_unit_name)))
                        unit_name = CheckForAlias(unparametrized_unit_name);
                }

                var ext = Path.GetExtension(unit_name);

                type = UnitTypes[ext];
                files = UnitFiles[unit_name].ToArray();
                Array.Sort(files, CompareUnitFiles);
            }

            var descriptor = UnitParser.FromFiles(UnitDescriptorTypes[type], files);
            var context = new UnitInstantiationContext();

            var pure_unit_name = UnitParser.GetUnitName(name, with_parameter: false);

            context.Substitutions["p"] = pure_unit_name;
            context.Substitutions["P"] = StringEscaper.Unescape(pure_unit_name);
            context.Substitutions["f"] = StringEscaper.UnescapePath(pure_unit_name);
            context.Substitutions["H"] = Environment.MachineName;

            var unit_parameter = UnitParser.GetUnitParameter(name);

            if (string.IsNullOrEmpty(unit_parameter))
            {
                unit_parameter = descriptor.DefaultInstance;
            }

            if (!string.IsNullOrEmpty(unit_parameter))
            {
                context.Substitutions["i"] = unit_parameter;
                context.Substitutions["I"] = StringEscaper.Unescape(unit_parameter);
                context.Substitutions["f"] = StringEscaper.UnescapePath(unit_parameter);
            }

            descriptor.InstantiateDescriptor(context);
            return descriptor;
        }

        public int CompareUnitFiles(UnitFile a, UnitFile b)
        {
            if (a == null || b == null)
            {
                if (a == null && b != null)
                    return 1;
                else if (a != null && b == null)
                    return -1;
                else
                    return 0;
            }

            if (a.FileName != b.FileName)
                return string.Compare(a.FileName, b.FileName);

            var path_list = new List<string>()
            {
                "(unknown)",
                "/run/sharpinit/generator.late/",
                "/usr/lib/sharpinit/system/", 
                "/usr/local/lib/sharpinit/system/", 
                "/run/sharpinit/generator/", 
                "/run/sharpinit/system/", 
                "/etc/sharpinit/system/", 
                "/run/sharpinit/generator.early/", 
                "/run/sharpinit/transient/", 
                "/run/sharpinit/system.control/", 
                "/etc/sharpinit/system.control/"
            };

            Func<string, int> get_path_index = (Func<string, int>)(p => 
                path_list.IndexOf(path_list.FirstOrDefault(p.StartsWith) ?? path_list.Last()));
            
            var a_index = get_path_index(a.Path);
            var b_index = get_path_index(b.Path);

            return a_index.CompareTo(b_index);
        }

        public static void InitializeTypes()
        {
            UnitTypes[".unit"] = typeof(Unit);
            UnitTypes[".service"] = typeof(ServiceUnit);
            UnitTypes[".target"] = typeof(TargetUnit);
            UnitTypes[".socket"] = typeof(SocketUnit);
            UnitTypes[".mount"] = typeof(MountUnit);
            UnitTypes[".slice"] = typeof(SliceUnit);
            UnitTypes[".scope"] = typeof(ScopeUnit);
            UnitTypes[".device"] = typeof(DeviceUnit);

            UnitDescriptorTypes[typeof(Unit)] = typeof(UnitDescriptor);
            UnitDescriptorTypes[typeof(ServiceUnit)] = typeof(ServiceUnitDescriptor);
            UnitDescriptorTypes[typeof(TargetUnit)] = typeof(UnitDescriptor);
            UnitDescriptorTypes[typeof(SocketUnit)] = typeof(SocketUnitDescriptor);
            UnitDescriptorTypes[typeof(MountUnit)] = typeof(MountUnitDescriptor);
            UnitDescriptorTypes[typeof(SliceUnit)] = typeof(UnitDescriptor);
            UnitDescriptorTypes[typeof(ScopeUnit)] = typeof(ExecUnitDescriptor);
            UnitDescriptorTypes[typeof(DeviceUnit)] = typeof(UnitDescriptor);
        }
    }
}
