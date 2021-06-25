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
    }

    public class GeneratedUnitFile : UnitFile
    {
        public GeneratedUnitFile(string name)
        {
            Properties = new Dictionary<string, List<string>>();
            UnitName = name;
            Extension = Path.GetExtension(name);
        }

        public GeneratedUnitFile WithProperty(string name, string value)
        {
            if (!Properties.ContainsKey(name))
                Properties[name] = new List<string>();

            Properties[name].Add(value);
            return this;
        }
    }

    public class OnDiskUnitFile : UnitFile
    {
        public string Path { get; set; }

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
