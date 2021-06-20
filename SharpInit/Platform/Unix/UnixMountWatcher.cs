using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;

using Mono.Unix;
using Mono.Unix.Native;

using SharpInit.Tasks;
using SharpInit.Units;

namespace SharpInit.Platform.Unix
{
    public static class UnixMountWatcher
    {
        public static event OnMountChanged MountChanged;
        public static Dictionary<int, UnixMount> Mounts = new Dictionary<int, UnixMount>();

        public static Dictionary<UnixMount, MountUnit> Units = new Dictionary<UnixMount, MountUnit>();

        public static void SynchronizeMountUnit(object sender, MountChangedEventArgs e)
        {
            var mount = e.Mount;
            var unit_name = StringEscaper.EscapePath(mount.MountPoint) + ".mount";

            if (!Units.ContainsKey(mount))
            {
                var generated_file = new GeneratedUnitFile(unit_name)
                    .WithProperty("Mount/What", mount.MountSource)
                    .WithProperty("Mount/Where", mount.MountPoint)
                    .WithProperty("Mount/Type", mount.MountType)
                    .WithProperty("Mount/Options", mount.MountOptions);
                
                UnitRegistry.IndexUnitFile(generated_file);
            }

            var unit = UnitRegistry.GetUnit<MountUnit>(unit_name);

            if (!Units.ContainsKey(mount))
                Units[mount] = unit;

            if (e.Change == MountChange.Added)
            {
                if (unit.CurrentState != UnitState.Activating && unit.CurrentState != UnitState.Active)
                    unit.GetExternalActivationTransaction().Execute();
            }
            else if (e.Change == MountChange.Changed)
            {
                // TODO: Implement
            }
            else if (e.Change == MountChange.Removed)
            {
                if (unit.CurrentState == UnitState.Activating || unit.CurrentState == UnitState.Active)
                    unit.GetExternalDeactivationTransaction().Execute();
            }
        }

        public static void StartWatching()
        {
            ParseMounts();

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
            }
        }

        private static void ParseMounts()
        {
            var fresh_mounts = File.ReadAllLines("/proc/self/mountinfo").Select(UnixMount.FromMountInfoLine);
            var validated_mounts = new HashSet<int>();

            foreach (var mount in fresh_mounts)
            {
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
                    MountChanged?.Invoke(null, new MountChangedEventArgs(Mounts[mount_id], MountChange.Removed));
                    Mounts.Remove(mount_id);
                }
            }
        }
    }
}