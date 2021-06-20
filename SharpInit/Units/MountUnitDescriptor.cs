using System;
using System.Collections.Generic;

namespace SharpInit.Units
{
    public class MountUnitDescriptor : ExecUnitDescriptor
    {
        [UnitProperty("Mount/@")]
        public string What { get; set; }
        [UnitProperty("Mount/@")]
        public string Where { get; set; }

        [UnitProperty("Mount/@")]
        public string Type { get; set; }
        [UnitProperty("Mount/@")]
        public string Options { get; set; }

        [UnitProperty("Mount/@", UnitPropertyType.Bool)]
        public bool SloppyOptions { get; set; }
        [UnitProperty("Mount/@", UnitPropertyType.Bool)]
        public bool LazyUnmount { get; set; }
        [UnitProperty("Mount/@", UnitPropertyType.Bool)]
        public bool ForceUnmount { get; set; }
        [UnitProperty("Mount/@", UnitPropertyType.Bool)]
        public bool ReadWriteOnly { get; set; }
        [UnitProperty("Mount/@", UnitPropertyType.IntOctal, DefaultValue = 493)] // 0755 in decimal
        public int DirectoryMode { get; set; }
    }
}