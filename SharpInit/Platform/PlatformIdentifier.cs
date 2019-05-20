using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace SharpInit.Platform
{
    /// <summary>
    /// Identifies a platform.
    /// </summary>
    public class PlatformIdentifier
    {
        private static PlatformIdentifier _cache;

        /// <summary>
        /// The platform codes that identify a particular platform. This list is ordered from most specific to least specific.
        /// </summary>
        public List<string> PlatformCodes { get; set; }

        internal PlatformIdentifier()
        {
            PlatformCodes = new List<string>();
        }

        internal PlatformIdentifier(params string[] identifiers)
        {
            PlatformCodes = identifiers.ToList();
        }

        /// <summary>
        /// Identifies the current platform, and returns a PlatformIdentifier that describes the current platform.
        /// </summary>
        /// <returns>A PlatformIdentifier that represents the current platform.</returns>
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