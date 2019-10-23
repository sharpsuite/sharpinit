using System;
using System.Collections.Generic;
using System.Text;

namespace SharpInit.Units
{
    public abstract class UnitFile
    {
        public Dictionary<string, List<string>> Properties { get; set; }
        public string Extension { get; set; }
        
    }

    public class OnDiskUnitFile : UnitFile
    {
        public string Path { get; set; }

        public OnDiskUnitFile(string path)
        {
            Path = path;
            Extension = System.IO.Path.GetExtension(path);
        }
    }
}
