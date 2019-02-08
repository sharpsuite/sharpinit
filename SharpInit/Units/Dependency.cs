using System;
using System.Collections.Generic;
using System.Text;

namespace SharpInit.Units
{
    public abstract class Dependency
    {
        public abstract DependencyType Type { get; }
        
        public string LeftUnit { get; set; }
        public string RightUnit { get; set; }

        public string SourceUnit { get; set; }
    }

    public enum DependencyType
    {
        Ordering, Requirement
    }
}
