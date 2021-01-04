using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SharpInit.Units
{
    public class UnitInstantiationContext
    {
        public Dictionary<string, string> Substitutions { get; set; }

        public UnitInstantiationContext() { Substitutions = new Dictionary<string, string>(); }

        public string Substitute(string value)
        {
            // substitute all percent sign specifiers
            value = value.Replace("%%", "%"); // this feels inadequate somehow....
                                               // TODO: check whether this logic works correctly

            if (Substitutions?.Any() == true)
            {
                foreach (var substitution in Substitutions)
                {
                    value = value .Replace($"%{substitution.Key}", substitution.Value);
                }
            }

            return value;
        }
    }
}
