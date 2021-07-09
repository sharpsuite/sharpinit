using System;
using System.Linq;

namespace SharpInit.Platform.Unix
{
    public class UnixMount
    {
        public int MountId { get; set; }
        public int ParentId { get; set; }
        public int StDevMajor { get; set; }
        public int StDevMinor { get; set; }
        public string RootPath { get; set; }
        public string MountPoint { get; set; }
        public string MountOptions { get; set; }
        public string MountType { get; set; }
        public string MountSource { get; set; }
        public string SuperOptions { get; set; }
        public string OptionalFields { get; set; }

        public string SourcePath { get; set; }
        public bool? Mounted { get; set; }

        public UnixMount() { }

        public static UnixMount FromMountInfoLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return null;

            try
            {
                var parts = line.Split(' ');

                if (parts.Length < 9)
                    return null;

                var mount = new UnixMount();

                if (int.TryParse(parts[0], out int mount_id))
                    mount.MountId = mount_id;

                if (int.TryParse(parts[1], out int parent_id))
                    mount.ParentId = parent_id;
                    
                if (int.TryParse(parts[2].Split(':')[0], out int st_major))
                    mount.StDevMajor = st_major;
                
                if (int.TryParse(parts[2].Split(':')[1], out int st_minor))
                    mount.StDevMinor = st_minor;
                
                mount.RootPath = parts[3];
                mount.MountPoint = parts[4];
                mount.MountOptions = parts[5];
                mount.OptionalFields = parts[6];
                
                var rest = string.Join(' ', parts.Skip(7)).TrimStart(' ', '-');
                var rest_parts = rest.Split(' ');

                mount.MountType = rest_parts[0];
                mount.MountSource = rest_parts[1];
                mount.SuperOptions = rest_parts[2];

                return mount;
            }
            catch
            {
                return null;
            }
        }

        public static UnixMount FromFstabLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
                return null;

            try
            {
                var parts = line.Split(new [] {' ', '\t'}, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                if (parts.Length < 4)
                    return null;

                var mount = new UnixMount();

                mount.MountId = -1;
                mount.MountSource = parts[0];
                mount.MountPoint = parts[1];
                mount.MountType = parts[2];
                mount.MountOptions = parts[3];

                return mount;
            }
            catch
            {
                return null;
            }
        }
    }
}