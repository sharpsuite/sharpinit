using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SharpInit.Platform
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = false, AllowMultiple = true)]
    sealed class SupportedOnAttribute : Attribute
    {
        public List<string> Platforms { get; set; }

        public SupportedOnAttribute(params string[] supported_platform)
        {
            Platforms = supported_platform.ToList();
        }
    }
}
