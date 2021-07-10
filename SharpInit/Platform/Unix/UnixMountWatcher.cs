using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;

using Mono.Unix;
using Mono.Unix.Native;

using SharpInit.Tasks;
using SharpInit.Units;

using NLog;

namespace SharpInit.Platform.Unix
{
    public static class UnixMountWatcher
    {
        static Logger Log = LogManager.GetCurrentClassLogger();

        public static event OnMountChanged MountChanged;
        public static Dictionary<int, UnixMount> Mounts = new Dictionary<int, UnixMount>();

        public static Dictionary<string, MountUnit> Units = new Dictionary<string, MountUnit>();

        public static ServiceManager ServiceManager { get; set; }

        public static void SynchronizeMountUnit(object sender, MountChangedEventArgs e)
        {
            var mount = e.Mount;
            var unit_name = StringEscaper.EscapePath(mount.MountPoint) + ".mount";

            if (!Units.ContainsKey(unit_name))
            {
                var generated_file = new GeneratedUnitFile(unit_name, source: mount.SourcePath)
                    .WithProperty("Mount/What", mount.MountSource)
                    .WithProperty("Mount/Where", mount.MountPoint)
                    .WithProperty("Mount/Type", mount.MountType)
                    .WithProperty("Mount/Options", mount.MountOptions);
                
                ServiceManager.Registry.IndexUnitFile(generated_file);
            }

            var unit = ServiceManager.Registry.GetUnit<MountUnit>(unit_name);

            if (!Units.ContainsKey(unit_name))
                Units[unit_name] = unit;

            if (mount.Mounted == true)
            {
                // TODO: These should go through UnitManager as should all other external events that should
                // lead to unit state changes.
                if (unit.CurrentState != UnitState.Activating && unit.CurrentState != UnitState.Active)
                    ServiceManager.Runner.Register(unit.GetExternalActivationTransaction("Externally managed mountpoint")).Enqueue();
            }
            else if (mount.Mounted == false)
            {
                if (unit.CurrentState == UnitState.Activating || unit.CurrentState == UnitState.Active)
                    ServiceManager.Runner.Register(unit.GetExternalDeactivationTransaction("Externally managed mountpoint")).Enqueue();
            }
        }

        public static void StartWatching()
        {
            ParseMounts();

            /* This seems to be broken.

            var mount_fd = Syscall.open("/proc/self/mountinfo", OpenFlags.O_RDONLY);
            var poll_fd = new Pollfd();

            poll_fd.fd = mount_fd;
            poll_fd.events = PollEvents.POLLIN | PollEvents.POLLERR;

            var poll_fd_arr = new [] { poll_fd };

            int poll_ret = 0;

            while ((poll_ret = Syscall.poll(poll_fd_arr, 1, 5)) >= 0)
            {
                if ((poll_fd.revents & poll_fd.events) > 0)
                    ParseMounts();
            } */

            while (true)
            {
                System.Threading.Thread.Sleep(10000);
                ParseMounts();
            }
        }

        public static void ParseFstab(string path = "/etc/fstab")
        {
            if (!File.Exists(path)) 
            {
                Log.Error($"Could not find fstab path \"{path}\"");
                return;
            }

            // This method assumes that it is only called once before any mounts are parsed from
            // /proc/self/mountinfo.
            var lines = File.ReadAllLines(path);

            foreach (var line in lines)
            {
                if (line.StartsWith('#'))
                    continue;
                
                var mount = UnixMount.FromFstabLine(line);
                
                if (mount == null)
                {
                    Log.Debug($"Skipping fstab line from {path}: \"{line}\"");
                    continue;
                }

                mount.Mounted = null;
                mount.SourcePath = path;
                MountChanged?.Invoke(null, new MountChangedEventArgs(mount, MountChange.Added));
            }
        }

        private static void ParseMounts()
        {
            var path = "/proc/self/mountinfo";

            if (!File.Exists(path))
            {
                Log.Error($"Could not find mountinfo path {path}");
                return;
            }

            var fresh_mounts = File.ReadAllLines("/proc/self/mountinfo").Select(UnixMount.FromMountInfoLine);
            var validated_mounts = new HashSet<int>();

            foreach (var mount in fresh_mounts)
            {
                mount.SourcePath = "/proc/self/mountinfo";
                mount.Mounted = true;
                validated_mounts.Add(mount.MountId);

                if (!Mounts.ContainsKey(mount.MountId))
                {
                    Mounts[mount.MountId] = mount;
                    MountChanged?.Invoke(null, new MountChangedEventArgs(mount, MountChange.Added));
                }
            }

            foreach (var mount_id in Mounts.Keys.ToList())
            {
                if (!validated_mounts.Contains(mount_id))
                {
                    Mounts[mount_id].Mounted = false;
                    MountChanged?.Invoke(null, new MountChangedEventArgs(Mounts[mount_id], MountChange.Removed));
                    Mounts.Remove(mount_id);
                }
            }
        }
    }
}