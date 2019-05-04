using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace SharpInit.Platform
{
    public class PlatformIdentifier
    {
        private static PlatformIdentifier _cache;
        public List<string> PlatformCodes { get; set; }

        internal PlatformIdentifier()
        {
            PlatformCodes = new List<string>();
        }

        internal PlatformIdentifier(params string[] identifiers)
        {
            PlatformCodes = identifiers.ToList();
        }

        public static PlatformIdentifier GetPlatformIdentifier()
        {
            if (_cache != null)
                return _cache;

            var ret = new PlatformIdentifier();

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                ret.PlatformCodes.Add("linux");
                ret.PlatformCodes.Add("unix");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                ret.PlatformCodes.Add("osx");
                ret.PlatformCodes.Add("unix");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                ret.PlatformCodes.Add("windows");
            }

            ret.PlatformCodes.Add("generic");

            return _cache = ret;
        }
    }
}