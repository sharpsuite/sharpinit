using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using Mono.Unix;
using Mono.Unix.Native;

using SharpInit.Tasks;
using SharpInit.Units;

using NLog;

namespace SharpInit.Platform.Unix
{
    public static class UdevEnumerator
    {
        static Logger Log = LogManager.GetCurrentClassLogger();

        public static event OnDeviceAdded DeviceAdded;
        public static event OnDeviceRemoved DeviceRemoved;
        public static event OnDeviceUpdated DeviceUpdated;
        
        public static ConcurrentDictionary<string, UdevDevice> Devices = new ConcurrentDictionary<string, UdevDevice>();
        public static Dictionary<string, DeviceUnit> Units = new Dictionary<string, DeviceUnit>();

        public static UnixSymlinkTools SymlinkTools = new UnixSymlinkTools(null);
        public static ServiceManager ServiceManager { get; set; }
        public static DateTime LastUdevScan { get; set; }
        public static bool Rescanning { get; set; }
        public static bool ScanNeeded { get; set; }
        
        public static void InitializeHandlers()
        {
            DeviceAdded += HandleNewDevice;
            DeviceRemoved += HandleDeviceRemoved;
            DeviceUpdated += HandleDeviceUpdated;

            var watcher = new FileSystemWatcher("/run/udev");
            watcher.NotifyFilter = NotifyFilters.Attributes
                                 | NotifyFilters.CreationTime
                                 | NotifyFilters.DirectoryName
                                 | NotifyFilters.FileName
                                 | NotifyFilters.LastWrite
                                 | NotifyFilters.Security
                                 | NotifyFilters.Size;

            watcher.Created += HandleUdevDirectoryInotify;
            watcher.Deleted += HandleUdevDirectoryInotify;
            watcher.Changed += HandleUdevDirectoryInotify;
            watcher.IncludeSubdirectories = true;
            watcher.EnableRaisingEvents = true;

            new Thread((ThreadStart) delegate
            {
                while (ServiceManager == null)
                    Thread.Sleep(500);
                
                while (true)
                {
                    while (!ScanNeeded)
                        Thread.Sleep(500);

                    ServiceManager.Runner.Register(new ScanUdevDevicesTask()).Enqueue().Wait();
                    ScanNeeded = false;
                    Thread.Sleep(500);
                }
            }).Start();
        }

        public static void WaitForUdevAndInitialize()
        {
            while (!System.IO.Directory.Exists("/run/udev/tags"))
                System.Threading.Thread.Sleep(100);

            if (System.IO.Directory.Exists("/run/udev/tags"))
            {
                try
                {
                    Platform.Unix.UdevEnumerator.ServiceManager = ServiceManager;
                    Platform.Unix.UdevEnumerator.InitializeHandlers();
                    
                    var task = new SharpInit.Tasks.ScanUdevDevicesTask();
                    ServiceManager.Runner.Register(task).Enqueue().Wait();
                }
                catch (Exception ex)
                {
                    Log.Warn(ex, $"Failed to retrieve devices from udevd.");
                }
            }
            else
            {
                Log.Info($"udevd is not running.");
            }
        }

        private static void HandleUdevDirectoryInotify(object sender, FileSystemEventArgs e)
        {
            ScanNeeded = true;
        }

        private static void HandleNewDevice(object sender, DeviceAddedEventArgs e)
        {
            var device = Devices[e.DevicePath];

            if (!Units.ContainsKey(e.DevicePath))
            {
                var escaped_path = StringEscaper.EscapePath(e.DevicePath);
                var escaped_name = escaped_path + ".device";

                var pretty_device_name = device.Properties.ContainsKey("ID_MODEL_FROM_DATABASE") ? device.Properties["ID_MODEL_FROM_DATABASE"].Last() :
                                         device.Properties.ContainsKey("ID_MODEL") ? device.Properties["ID_MODEL"].Last() :
                                         device.SysPath;
                
                var device_unit_file = new GeneratedUnitFile(escaped_name).WithProperty("Unit/Description", pretty_device_name);

                if (device.Properties.ContainsKey("SYSTEMD_WANTS"))
                {
                    var unit_names = device.Properties["SYSTEMD_WANTS"].SelectMany(w => w.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                    var unit_names_instantiated = unit_names.Select(name => name.Contains('@') ? name.Replace("@", $"@{escaped_path}") : name);

                    foreach (var wanted_unit_name in unit_names_instantiated)
                        device_unit_file.WithProperty("Unit/Wants", wanted_unit_name);
                }

                ServiceManager.Registry.IndexUnitFile(device_unit_file);
                var device_unit = ServiceManager.Registry.GetUnit<DeviceUnit>(device_unit_file.UnitName);

                if (device_unit == null)
                {
                    Log.Info($"Failed to create device unit for device with syspath {e.DevicePath}");
                    return;
                }

                Units[e.DevicePath] = device_unit;

                if (device.Properties.ContainsKey("SYSTEMD_ALIAS"))
                {
                    foreach (var alias_name in device.Properties["SYSTEMD_ALIAS"])
                        ServiceManager.Registry.AliasUnit(device_unit, alias_name);
                }

                if (device.DevPaths?.Any() == true)
                {
                    var dev_paths = device.DevPaths.Select(d => $"/dev/{d}").ToList();
                    var symlinked_dev_paths = new List<string>();

                    foreach (var p in dev_paths)
                    {
                        if (SymlinkTools.IsSymlink(p))
                        {
                            var target = SymlinkTools.GetTarget(p);

                            if (!target.StartsWith("/"))
                                target = Path.GetFullPath(target, Path.GetDirectoryName(p));

                            symlinked_dev_paths.Add(target);
                        }
                    }

                    dev_paths.AddRange(symlinked_dev_paths);
                    dev_paths = dev_paths.Distinct().ToList();

                    foreach (var dev_path in dev_paths)
                    {
                        var dev_escaped_name = StringEscaper.EscapePath(dev_path) + ".device";
                        ServiceManager.Registry.AliasUnit(device_unit, dev_escaped_name);
                    }
                }
                
                device_unit.IsActive = !device.Properties.ContainsKey("SYSTEMD_READY") || 
                                        device.Properties["SYSTEMD_READY"].Last().Trim() == "1";
            }

            var dev_unit = Units[e.DevicePath];

            if (dev_unit.IsActive)
            {
                ServiceManager.Runner.Register(GenerateActivation(dev_unit)).Enqueue();
            }
        }

        private static Transaction GenerateActivation(DeviceUnit unit)
        {
            var tx1 = unit.GetExternalActivationTransaction();
            return tx1;
            var tx2 = LateBoundUnitActivationTask.CreateActivationTransaction(unit);

            return new Transaction(tx1, tx2);
        }

        private static Transaction GenerateDeactivation(DeviceUnit unit)
        {
            var tx1 = unit.GetExternalDeactivationTransaction();
            return tx1;
            var tx2 = LateBoundUnitActivationTask.CreateDeactivationTransaction(unit);

            return new Transaction(tx1, tx2);
        }

        private static void HandleDeviceUpdated(object sender, DeviceUpdatedEventArgs e)
        {
            var device = Devices[e.DevicePath];

            if (!Units.ContainsKey(e.DevicePath))
            {
                return;
            }
            
            var dev_unit = Units[e.DevicePath];
            var next_active = !device.Properties.ContainsKey("SYSTEMD_READY") || 
                               device.Properties["SYSTEMD_READY"].Last().Trim() == "1";

            if (!dev_unit.IsActive && next_active)
            {
                ServiceManager.Runner.Register(GenerateActivation(dev_unit)).Enqueue();
            }
            else if (dev_unit.IsActive && !next_active)
            {
                ServiceManager.Runner.Register(GenerateDeactivation(dev_unit)).Enqueue();
            }
        }

        private static void HandleDeviceRemoved(object sender, DeviceRemovedEventArgs e)
        {
            if (!Units.ContainsKey(e.DevicePath))
            {
                return;
            }

            var dev_unit = Units[e.DevicePath];

            ServiceManager.Runner.Register(GenerateDeactivation(dev_unit)).Enqueue();
        }

        public static void ScanDevicesByTag(string tag)
        {
            lock (Units)
            {
                if (!Directory.Exists($"/run/udev/tags/{tag}"))
                {
                    Log.Warn($"No devices found for tag {tag}, or udev is non-systemd");
                    return;
                }

                var files = Directory.GetFiles($"/run/udev/tags/{tag}");
                var devices_hit = new List<string>();

                foreach (var file in files)
                {
                    var device_id = Path.GetFileName(file);
                    var dev = UdevDevice.FromDeviceId(device_id);

                    if (dev != null && !string.IsNullOrWhiteSpace(dev.SysPath))
                    {
                        devices_hit.Add(dev.SysPath);
                    }
                    else
                    {
                        Log.Warn($"Could not identify device \"{file}\"");
                        continue;
                    }

                    if (Devices.ContainsKey(dev.SysPath))
                    {
                        //Log.Debug($"Reparsing {dev.SysPath}");
                        Devices[dev.SysPath].ParseDatabaseIfExists();
                        DeviceUpdated?.Invoke(null, new DeviceUpdatedEventArgs(dev.SysPath));
                        continue;
                    }

                    Log.Debug($"Discovered new device {dev.SysPath}");
                    Devices[dev.SysPath] = dev;
                    dev.ParseDatabaseIfExists();
                    DeviceAdded?.Invoke(null, new DeviceAddedEventArgs(dev.SysPath));
                }

                var devices_to_remove = new List<string>();

                foreach (var pair in Devices)
                {
                    if (!devices_hit.Contains(pair.Key) && pair.Value.Tags.Contains(tag))
                    {
                        Log.Info($"Device {pair.Value.SysPath} has been removed");
                        devices_to_remove.Add(pair.Key);
                        DeviceRemoved?.Invoke(null, new DeviceRemovedEventArgs(pair.Key));
                    }
                }

                devices_to_remove.ForEach(dev => Devices.TryRemove(dev, out UdevDevice _));
            }
        }
    }

    public class UdevDevice
    {
        static Logger Log = LogManager.GetCurrentClassLogger();
        
        public string Name { get; set; }
        public string DeviceId { get; set; }
        public string SysPath { get; private set; }
        public string Subsystem { get; private set; }

        public Dictionary<char, List<string>> DatabaseEntries { get; set; }

        public long InitializedUsec { get; set; }
        public int DevlinkPriority { get; set; }
        public Dictionary<string, List<string>> Properties { get; set; }

        public List<string> DevPaths { get; set; }
        public List<string> Tags { get; set; }

        public UdevDevice()
        {
            DatabaseEntries = new Dictionary<char, List<string>>();
            Properties = new Dictionary<string, List<string>>();
            DevPaths = new List<string>();
            Tags = new List<string>();
        }

        private void SetSysPath(string sys_path)
        {
            while (UdevEnumerator.SymlinkTools.IsSymlink(sys_path))
            {
                var target = UdevEnumerator.SymlinkTools.GetTarget(sys_path);

                if (target[0] != '/')
                {
                    target = Path.GetFullPath(target, Path.GetDirectoryName(sys_path));
                }

                sys_path = target;
            }

            if (!sys_path.StartsWith("/sys"))
                throw new InvalidOperationException();

            var remainder = sys_path.Substring("/sys".Length);

            if (remainder == "/")
                throw new InvalidOperationException();

            if (remainder.StartsWith("/devices/"))
            {
                if (!File.Exists(sys_path + "/uevent"))
                    throw new InvalidOperationException();
            }
            else
                if (!Directory.Exists(sys_path))
                    throw new InvalidOperationException();

            if (UdevEnumerator.SymlinkTools.IsSymlink($"{sys_path}/subsystem"))
            {
                var target = UdevEnumerator.SymlinkTools.ResolveSymlink($"{sys_path}/subsystem");
                Log.Debug($"device with syspath {sys_path} has subsystem symlink to {target}");

                if (!string.IsNullOrWhiteSpace(target) && target.StartsWith("/sys/class/"))
                {
                    Subsystem = target.Substring("/sys/class/".Length);
                }
            }
            else
            {
                Log.Debug($"device with syspath {sys_path} has no subsystem symlink");
            }

            SysPath = sys_path;
        }

        public static UdevDevice FromSysPath(string path, string device_id = null)
        {
            var dev = new UdevDevice();

            try
            {
                dev.SetSysPath(path);
                dev.DeviceId = device_id;
            }
            catch
            {
                return null;
            }

            return dev;
        }

        public void ParseDatabaseIfExists()
        {
            if (File.Exists($"/run/udev/data/{DeviceId}"))
                ParseDatabase($"/run/udev/data/{DeviceId}");
        }

        public static UdevDevice FromDevNum(string devnum)
        {
            if (string.IsNullOrWhiteSpace(devnum))
                return null;
            
            if (devnum[0] != 'b' && devnum[0] != 'c')
                return null;

            var parts = devnum.Substring(1).Split(':');

            if (parts.Length != 2)
                return null;
            
            if (!parts.All(p => int.TryParse(p, out int _)))
                return null;
            
            return FromDevNum(int.Parse(parts[0]), int.Parse(parts[1]), devnum[0] == 'b');
        }

        public static UdevDevice FromDevNum(int major, int minor, bool block)
        {
            var dev = FromSysPath($"/sys/dev/{(block ? "block" : "char")}/{major}:{minor}", $"{(block ? "b" : "c")}{major}:{minor}");
            return dev;
        }

        public static UdevDevice FromNetworkInterfaceNumber(int if_num)
        {
            var if_name = $"n{if_num}";
            var iface = FromNetworkInterfaceName($"n{if_num}");

            if (iface != null)
                return iface;

            int failed = 0;

            start_label:

            if (failed > 2)
                return null;

            if (iface == null)
            {
                if (if_index_map.ContainsKey(if_name))
                {
                    // check whether entry is valid
                    var prospective_if_path = if_index_map[if_name];
                    
                    if (!Directory.Exists(prospective_if_path) || !File.Exists($"{prospective_if_path}/ifindex"))
                    {
                        BuildIfIndexMap();
                        failed++;
                        goto start_label;
                    }

                    var current_if_index = File.ReadAllText($"{prospective_if_path}/ifindex").Trim();

                    if (if_name != $"n{current_if_index}")
                    {
                        BuildIfIndexMap();
                        failed++;
                        goto start_label;
                    }

                    iface = FromSysPath(prospective_if_path);

                    if (iface != null)
                        return iface;
                    
                    failed++;
                    goto start_label;
                }
                else
                {
                    BuildIfIndexMap();
                    failed++;
                    goto start_label;
                }
            }

            return null;
        }

        static Dictionary<string,string> if_index_map = new Dictionary<string, string>();
        static void BuildIfIndexMap()
        {
            if_index_map.Clear();
            
            if (!Directory.Exists("/sys/class/net"))
                return;

            try
            {
                var dirs = Directory.GetDirectories("/sys/class/net");

                foreach (var dir in dirs)
                {
                    var target = dir;
                    if (UdevEnumerator.SymlinkTools.IsSymlink(dir))
                    {
                        target = UdevEnumerator.SymlinkTools.GetTarget(dir);
                        target = Path.GetFullPath(target, $"/sys/class/net");
                    }

                    if (File.Exists($"{dir}/ifindex"))
                    {
                        if_index_map[$"n{File.ReadAllText($"{dir}/ifindex").Trim()}"] = target;
                    }
                }
            }
            catch
            {
                return;
            }
        }

        public static UdevDevice FromNetworkInterfaceName(string if_name)
        {
            var path = $"/sys/class/net/{if_name}";
            
            if (!Directory.Exists(path))
            {
                return null;
            }

            return FromSysPath(path, if_name);
        }

        public static UdevDevice FromSubsystem(string subsystem, string sysname)
        {
            UdevDevice device = null;
            var device_id = $"+{subsystem}:{sysname}";

            switch (subsystem)
            {
                case "subsystem":
                    foreach (var s in new [] { "/sys/subsystem/", "/sys/bus/", "/sys/class/" })
                    {
                        if ((device = FromSysPath(s + sysname, device_id)) != null)
                            return device;
                    }
                    break;
                case "module":
                    if ((device = FromSysPath("/sys/module/" + sysname, device_id)) != null)
                        return device;
                    break;
            }

            var translated_sysname = sysname.Replace('/', '!');

            if ((device = FromSysPath($"/sys/bus/{subsystem}/devices/{translated_sysname}", device_id)) != null)
                return device;
            
            if ((device = FromSysPath($"/sys/subsystem/{subsystem}/devices/{translated_sysname}", device_id)) != null)
                return device;

            if ((device = FromSysPath($"/sys/class/{subsystem}/{translated_sysname}", device_id)) != null)
                return device;

            if ((device = FromSysPath($"/sys/firmware/{subsystem}/{sysname}", device_id)) != null)
                return device;

            return null;
        }

        public static UdevDevice FromDeviceId(string device_id)
        {
            switch(device_id[0])
            {
                case 'b':
                case 'c':
                    return FromDevNum(device_id);
                case 'n':
                    var index = int.Parse(device_id.Substring(1));
                    return FromNetworkInterfaceNumber(index);
                case '+':
                    var subsystem = device_id.Substring(1).Split(':')[0];
                    var sysname = device_id.Substring(subsystem.Length + 2);
                    return FromSubsystem(subsystem, sysname);
                default:
                    return null;
            }
        }

        public void ParseDatabase(string db_path)
        {
            if (!File.Exists(db_path))
                return;

            DatabaseEntries.Clear();
            var contents = File.ReadAllLines(db_path);

            foreach (var line in contents)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var key = line[0];
                var value = line.Substring(2);

                if (!DatabaseEntries.ContainsKey(key))
                    DatabaseEntries[key] = new List<string>();
                
                DatabaseEntries[key].Add(value);
            }

            ParseProperties();
        }

        private void ParseProperties()
        {
            Properties.Clear();
            DevPaths.Clear();
            Tags.Clear();

            if (DatabaseEntries.ContainsKey('G'))
                Tags.AddRange(DatabaseEntries['G']);

            if (DatabaseEntries.ContainsKey('Q'))
                Tags.AddRange(DatabaseEntries['Q']);
            
            if (DatabaseEntries.ContainsKey('I'))
                InitializedUsec = long.Parse(DatabaseEntries['I'].First());

            if (DatabaseEntries.ContainsKey('S'))
            {
                var dev_paths = DatabaseEntries['S'];

                foreach (var dev_path in dev_paths)
                {
                    var patched_dev_path = dev_path;
                    if (!dev_path.StartsWith("/dev"))
                        patched_dev_path = "/dev/" + (dev_path.TrimStart('/'));
                    DevPaths.Add(patched_dev_path);
                }
            }

            if (DatabaseEntries.ContainsKey('E'))
            {
                foreach (var str in DatabaseEntries['E'])
                {
                    var parts = str.Split('=', StringSplitOptions.RemoveEmptyEntries);
                    var key = "";
                    var value = "";

                    if (parts.Length == 0)
                    {
                        continue;
                    }
                    
                    key = parts[0];
                    value = str.Substring(key.Length + 1);

                    if (!Properties.ContainsKey(key))
                        Properties[key] = new List<string>();
                    
                    Properties[key].Add(value);
                }
            }

            if (DatabaseEntries.ContainsKey('L'))
                DevlinkPriority = int.Parse(DatabaseEntries['L'][0]);
        }
    }
}