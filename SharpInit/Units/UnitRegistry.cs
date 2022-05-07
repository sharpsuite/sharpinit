using SharpInit.Tasks;
using SharpInit.Platform;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Mono.Unix.Native;

namespace SharpInit.Units
{
    public class UnitRegistry
    {
        public static Dictionary<string, Type> UnitTypes = new Dictionary<string, Type>();
        public static Dictionary<Type, Type> UnitDescriptorTypes = new Dictionary<Type, Type>();
        
        public static List<string> DefaultScanDirectories { get; set; }

        public event OnUnitAdded UnitAdded;

        public Logger Log = LogManager.GetCurrentClassLogger();

        public Dictionary<string, List<UnitFile>> UnitFiles = new Dictionary<string, List<UnitFile>>();
        public Dictionary<string, Unit> Units = new Dictionary<string, Unit>();
        public Dictionary<string, List<string>> Aliases = new Dictionary<string, List<string>>();

        // service.d // test-first-@.service
        public Dictionary<string, List<UnitFile>> DropInUnitFiles = new Dictionary<string, List<UnitFile>>();

        public DependencyGraph<OrderingDependency> OrderingDependencies = new DependencyGraph<OrderingDependency>();
        public DependencyGraph<RequirementDependency> RequirementDependencies = new DependencyGraph<RequirementDependency>();

        public List<string> ScanDirectories = new List<string>();
        private ServiceManager ServiceManager { get; set; }

        private ISymlinkTools SymlinkTools => ServiceManager.SymlinkTools;

        internal UnitRegistry(ServiceManager manager)
        {
            ServiceManager = manager;
            BuildDefaultScanDirectories();
        }

        private void BuildDefaultScanDirectories()
        {
            // TODO: Consider platform.

            if (Program.IsUserManager)
            {
                var home_dir = new Mono.Unix.UnixUserInfo(Syscall.getuid()).HomeDirectory;
                DefaultScanDirectories = new List<string>()
                {
                    $"{home_dir}/.config/sharpinit/units",
                    "/etc/sharpinit/user",
                    "/lib/sharpinit/user"
                };
            }
            else
            {
                DefaultScanDirectories = new List<string>()
                {
                    "/etc/sharpinit/system",
                    "/usr/local/sharpinit/system"
                };
            }
            
            Log.Debug($"Default scan directories: {string.Join("; ", DefaultScanDirectories)}");
        }

        public bool InstallUnit(string unit_name, int cycle = 0) 
        {
            if (cycle > 10)
                return false;

            var symlinks = ServiceManager.SymlinkTools;
            var unit = GetUnit(unit_name);
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

            var aliases = unit.Descriptor.Alias;
            
            foreach (var alias in aliases)
            {
                var target_path = $"/etc/sharpinit/units/{alias}";

                if (File.Exists(target_path))
                {
                    Log.Warn($"Skipping enablement symlink {unit_source_path} => {target_path} because target file already exists");
                    continue;
                }

                if (!symlinks.CreateSymlink(unit_source_path, target_path, true)) 
                    Log.Warn($"Failed to enable symlink {unit_source_path} => {target_path}");
            }

            var also = unit.Descriptor.Also;

            foreach (var other_unit_to_install in also)
            {
                InstallUnit(other_unit_to_install, cycle + 1);
            }

            return true;
        }

        public bool AliasUnit(Unit unit, string alias)
        {
            if (Units.ContainsKey(alias))
                return false;
            
            if (!Units.ContainsValue(unit))
                return false;
            
            return AliasUnit(unit.UnitName, alias);
        }

        public bool AliasUnit(string original, string alias)
        {
            if (!Aliases.ContainsKey(alias))
                Aliases[alias] = new List<string>();
            
            if (!Aliases[alias].Contains(original))
                Aliases[alias].Add(original);
            
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
            var env_units_parts = (Environment.GetEnvironmentVariable("SHARPINIT_UNIT_PATH") ?? "").Split(':', StringSplitOptions.RemoveEmptyEntries);

            ScanDirectories.Clear();
            ScanDirectories.AddRange(DefaultScanDirectories);
            ScanDirectories.AddRange(env_units_parts.Where(Directory.Exists));
            Log.Debug($"Scanning these directories for unit files: {string.Join("; ", ScanDirectories)}");

            ReloadPre();

            foreach (var dir in ScanDirectories)
            {
                if (!Directory.Exists(dir))
                {
                    Log.Warn($"Asked to scan nonexistent directory {dir}");
                    continue;
                }

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
            var directory_name = path.TrimEnd('/');
        
            var directories = recursive ? Directory.GetDirectories(path) : new string[0];
            var files = Directory.GetFiles(path);

            int count = 0;

            foreach (var file in files)
            {
                Log.Debug($"Indexing unit file {file}");
                if (file.EndsWith(".conf") && directory_name.EndsWith(".d"))
                {
                    IndexDropInFileByPath(file);
                    continue;
                }
                
                if (!UnitTypes.Any(type => file.EndsWith(type.Key))) 
                {
                    continue;
                }
                
                if (SymlinkTools.IsSymlink(file))
                {
                    var target = SymlinkTools.ResolveSymlink(file);
                    var target_unit_name = UnitParser.GetUnitName(target, with_parameter: true);
                    var symlinked_name = UnitParser.GetUnitName(file, with_parameter: true);

                    Log.Debug($"!Symlink detected from {file} to {target}");

                    var fileinfo = new FileInfo(file);

                    // If symlinked to an empty file, disable the unit.
                    if (fileinfo.Length == 0)
                    {
                        IndexUnitFile(new GeneratedUnitFile(symlinked_name, destroy_on_reload: true).WithProperty("Disabled", "yes"));
                        continue;
                    }

                    // Check if this unit file has already been indexed or not.
                    if (!UnitFiles.Any(unit_files => unit_files.Value.OfType<OnDiskUnitFile>().Any(unit_file => unit_file.Path == target)))
                    {
                        // If the file hasn't been indexed yet, do so. This check prevents symlinked files from being parsed more than once.
                        IndexUnitByPath(target);
                    }
                    
                    //if (/*Units.Any(u => u.Key == symlinked_name) && !Aliases.ContainsKey(symlinked_name))
                    {
                        // detect .wants, .requires
                        var directory_maps = new Dictionary<string, string>()
                        {
                            {".wants", "Unit/Wants" },
                            {".requires", "Unit/Requires" },
                        };

                        //var directory_name = Path.GetDirectoryName(file);
                        bool in_mapped_dir = false;

                        foreach (var directory_mapping in directory_maps) 
                        {
                            if (directory_name.EndsWith(directory_mapping.Key))
                            {
                                in_mapped_dir = true;
                                // If we find a directory mapping (like default.target.wants/sshd.service), create an in-memory unit file to
                                // store the mapped property (in this example, it would be a unit file for default.target that contains
                                // Wants=sshd.service.
                                var unit_name = Path.GetFileName(directory_name);
                                unit_name = unit_name.Substring(0, unit_name.Length - directory_mapping.Key.Length);
                                var temp_unit_file = new GeneratedUnitFile(unit_name, destroy_on_reload: true).WithProperty(directory_mapping.Value, UnitParser.GetUnitName(file, with_parameter: true));
                                IndexUnitFile(temp_unit_file);
                                Log.Info($"{symlinked_name} has a directory-mapped dependency on {unit_name}");
                            }
                        }
                        if (!in_mapped_dir)
                        {
                            Log.Info($"{symlinked_name} is aliased to {target_unit_name}");
                            AliasUnit(target_unit_name, symlinked_name);
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

        public bool IndexDropInFile(UnitFile file)
        {
            var drop_in_scope = Path.GetFileName(Path.GetDirectoryName(file.Path));
            drop_in_scope = drop_in_scope;

            if (!DropInUnitFiles.ContainsKey(drop_in_scope))
                DropInUnitFiles[drop_in_scope] = new List<UnitFile>();
            
            if (file is OnDiskUnitFile)
                DropInUnitFiles[drop_in_scope].RemoveAll(u => 
                    u is OnDiskUnitFile && 
                    (u as OnDiskUnitFile).Path == (file as OnDiskUnitFile).Path);

            DropInUnitFiles[drop_in_scope].Add(file);

            return true;
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

        public bool IndexDropInFileByPath(string path)
        {
            path = Path.GetFullPath(path);
            path = SymlinkTools.ResolveSymlink(path);

            var unit_file = UnitParser.ParseFile(path);

            if (unit_file == null)
                return false;
            
            return IndexDropInFile(unit_file);            
        }

        public bool IndexUnitByPath(string path)
        {
            path = Path.GetFullPath(path);
            path = SymlinkTools.ResolveSymlink(path);

            var unit_file = UnitParser.ParseFile(path);

            if (unit_file == null)
            {
                Log.Warn($"Failed to parse unit file at path {path}");
                return false;
            }

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

        private UnitFile[] GetRelevantDropIns(string unit_name)
        {
            /*
             * unit name has dashes: enumerate all possible prefixes
             * unit name is instantiated: enumerate un-instantiated version
             * enumerate unit type drop in scope
             * 
             */

            var drop_in_scopes_to_search = new List<string>();
            var unparametrized_unit_name = UnitParser.GetUnitName(unit_name, with_parameter: false);
            var parametrized_unit_name = UnitParser.GetUnitName(unit_name, with_parameter: true);
            var unparametrized_unit_name_without_ext = Path.GetFileNameWithoutExtension(unparametrized_unit_name);
            var parametrized_unit_name_without_ext = Path.GetFileNameWithoutExtension(parametrized_unit_name);
            var extension = Path.GetExtension(unit_name);

            drop_in_scopes_to_search.Add(parametrized_unit_name);
            if (parametrized_unit_name != unparametrized_unit_name)
                drop_in_scopes_to_search.Add(unparametrized_unit_name);
            
            if (unit_name.Contains('-'))
            {
                var parts = unparametrized_unit_name_without_ext.Split('-');

                for (int i = parts.Length - 1; i > 0; i--)
                {
                    var name = string.Join('-', parts.Take(i)) + '-';
                    if (unit_name.Contains('@'))
                        name += '@';
                    
                    name += extension;
                    drop_in_scopes_to_search.Add(name);
                }
            }

            if (unit_name.Contains('@') && !string.IsNullOrWhiteSpace(UnitParser.GetUnitParameter(unit_name)))
            {
                var parameter = UnitParser.GetUnitParameter(unit_name);
                drop_in_scopes_to_search.AddRange(
                    drop_in_scopes_to_search.Select(scope => 
                        scope.Replace("@", $"@{parameter}")));
            }
            
            drop_in_scopes_to_search.Add(extension.Substring(1));
            drop_in_scopes_to_search = drop_in_scopes_to_search.Select(scope => scope + ".d").ToList();

            var files = new List<UnitFile>();
            foreach (var scope in drop_in_scopes_to_search)
                if (DropInUnitFiles.ContainsKey(scope))
                    files.AddRange(DropInUnitFiles[scope]);

            return files.ToArray();
        }

        public UnitDescriptor GetUnitDescriptor(string name)
        {
            string unit_name = name;
            var parametrized_unit_name = UnitParser.GetUnitName(name, with_parameter: true);
            var unparametrized_unit_name = UnitParser.GetUnitName(name, with_parameter: false);

            Type type;
            UnitFile[] files;
            UnitFile[] drop_ins;

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
                
                drop_ins = GetRelevantDropIns(unit_name);
                files = files.Concat(drop_ins).ToArray();
            }

            var descriptor = UnitParser.FromFiles(UnitDescriptorTypes[type], files);
            var context = new UnitInstantiationContext();

            var pure_unit_name = UnitParser.GetUnitName(name, with_parameter: false);

            context.Substitutions["p"] = pure_unit_name;
            context.Substitutions["P"] = StringEscaper.Unescape(pure_unit_name);
            context.Substitutions["f"] = StringEscaper.UnescapePath(pure_unit_name);
            context.Substitutions["H"] = Environment.MachineName;
            context.Substitutions["u"] = Environment.UserName;

            if (PlatformUtilities.CurrentlyOn("unix"))
            {
                context.Substitutions["U"] = Syscall.getuid().ToString();
            }

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

            bool IsDropInFile(UnitFile a) => Path.GetDirectoryName(a.Path).EndsWith(".d");

            if (IsDropInFile(a) && !IsDropInFile(b))
                return 1;
            if (!IsDropInFile(a) && IsDropInFile(b))
                return -1;
            
            if (IsDropInFile(a) && IsDropInFile(b))
            {
                /*
                 * Each drop-in file must contain appropriate section headers. For instantiated units, this logic will first
                 * look for the instance ".d/" subdirectory (e.g. "foo@bar.service.d/") and read its ".conf" files, followed
                 * by the template ".d/" subdirectory (e.g. "foo@.service.d/") and the ".conf" files there. Moreover for unit
                 * names containing dashes ("-"), the set of directories generated by repeatedly truncating the unit name
                 * after all dashes is searched too. Specifically, for a unit name foo-bar-baz.service not only the regular
                 * drop-in directory foo-bar-baz.service.d/ is searched but also both foo-bar-.service.d/ and foo-.service.d/.
                 * This is useful for defining common drop-ins for a set of related units, whose names begin with a common
                 * prefix. This scheme is particularly useful for mount, automount and slice units, whose systematic naming
                 * structure is built around dashes as component separators. Note that equally named drop-in files further
                 * down the prefix hierarchy override those further up, i.e. foo-bar-.service.d/10-override.conf overrides
                 * foo-.service.d/10-override.conf.
                 *
                 * test-first-second@full.service
                 * test-first-second@full.service.d/10-override.conf
                 * test-first-second@.service.d/10-override.conf
                 * test-first-second.service.d/10-override.conf
                 * test-first-.service.d/10-override.conf
                 * service.d/10-override.conf
                 */

                var directory_level = 0;
                
                
            }

            // Does not apply to drop-in files
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
