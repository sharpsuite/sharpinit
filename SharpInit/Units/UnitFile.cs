using System;
using System.Collections.Generic;
using System.IO;

namespace SharpInit.Units
{
    public abstract class UnitFile
    {
        public Dictionary<string, List<string>> Properties { get; set; }

        public string UnitName { get; set; }
        public string Extension { get; set; }
        public string FileName { get; set; }

        // Used in unit file ordering.
        public string Path { get; set; }
    }

    public class GeneratedUnitFile : UnitFile
    {
        public enum GeneratedUnitFilePriority
        {
            Early, Normal, Late
        }

        public GeneratedUnitFilePriority Priority { get; private set; }
        public string Source { get; set; }

        public GeneratedUnitFile(string name, GeneratedUnitFilePriority priority = GeneratedUnitFilePriority.Normal, string source = null)
        {
            Properties = new Dictionary<string, List<string>>();
            Priority = priority;
            UnitName = name;
            Extension = System.IO.Path.GetExtension(name);
            Source = source;

            FileName = name;
            Path = $"/run/sharpinit/generator";

            switch (priority)
            {
                case GeneratedUnitFilePriority.Early:
                    Path += ".early";
                    break;
                case GeneratedUnitFilePriority.Late:
                    Path += ".late";
                    break;
            }

            Path += $"/{UnitName}";
        } 

        public GeneratedUnitFile WithProperty(string name, string value)
        {
            if (!Properties.ContainsKey(name))
                Properties[name] = new List<string>();

            Properties[name].Add(value);
            return this;
        }

        public override string ToString()
        {
            if (!string.IsNullOrWhiteSpace(Source))
                return $"Unit file generated from \"{Source}\"";
            else
                return $"Generated unit file";
        }
    }

    public class OnDiskUnitFile : UnitFile
    {
        public OnDiskUnitFile(string path)
        {
            Path = path;
            UnitName = UnitRegistry.GetUnitName(path, with_parameter: true);
            Extension = System.IO.Path.GetExtension(UnitName);
        }

        public override string ToString()
        {
            return $"Unit file loaded from \"{Path}\"";
        }
    }
}
