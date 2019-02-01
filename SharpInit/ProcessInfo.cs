using SharpInit.Units;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

using Mono.Unix.Native;
using System.Runtime.InteropServices;

namespace SharpInit
{
    public class ProcessInfo
    {
        public static bool PlatformSupportsSignaling
        {
            get
            {
                if (_platform_signaling_support.HasValue)
                    return _platform_signaling_support.Value;

                _platform_signaling_support = new bool?(
                    RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
                    RuntimeInformation.IsOSPlatform(OSPlatform.OSX));

                return _platform_signaling_support.Value;
            }
        }
        private static bool? _platform_signaling_support = new bool?();

        public Unit SourceUnit { get; set; }
        public int Id { get; set; }

        public Process Process { get; set; }

        public ProcessInfo(Process proc, Unit source_unit = null)
        {
            Id = proc.Id;
            Process = proc;

            SourceUnit = source_unit;
        }

        public bool SendSignal(Signum signal)
        {
            if (!PlatformSupportsSignaling)
                return false;

            Syscall.kill(Id, signal);
            return true;
        }
    }
}
