using SharpInit.Units;
using SharpInit.Platform;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

using Mono.Unix.Native;
using System.Runtime.InteropServices;
using System.Threading;

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
        public ServiceManager ServiceManager { get; set; }
        public IProcessHandler ProcessHandler { get; set; }
        public int Id { get; set; }
        public int ExitCode { get; set; }
        public bool HasExited { get; set; }
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

        public bool WaitForExit(TimeSpan timeout)
        {
            if (HasExited)
                return true;

            var waiter = new ManualResetEvent(false);
            OnProcessExit handler = null;
            handler = (OnProcessExit)((pid, code) => 
            {
                if (pid != this.Id)
                    return;

                waiter.Set();
                ProcessHandler.ProcessExit -= handler;
            });

            ProcessHandler.ProcessExit += handler;
            if (HasExited)
            {
                handler(this.Id, 0);
                return true;
            }

            if (waiter.WaitOne(timeout))
                return true;
            
            handler(this.Id, 0);
            return false;
        }
    }
}
