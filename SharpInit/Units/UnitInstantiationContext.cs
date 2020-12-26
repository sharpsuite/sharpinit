using System;
using System.Collections.Generic;
using System.Text;

namespace SharpInit.Units
{
    public class UnitInstantiationContext
    {
        public Dictionary<string, string> Substitutions { get; set; }

        public UnitInstantiationContext() { Substitutions = new Dictionary<string, string>(); }
    }
}
