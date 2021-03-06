﻿using SharpInit.Units;
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
        public bool ExitProcessed { get; set; }
        public Process Process { get; set; }

        public ProcessInfo(Process proc, Unit source_unit = null)
        {
            Id = proc.Id;
            Process = proc;

            SourceUnit = source_unit;
        }

        private ProcessInfo() {}

        public static ProcessInfo FromPid(int pid, Unit source = null)
        {
            var proc_info = new ProcessInfo();
            proc_info.Id = pid;
            proc_info.SourceUnit = source;

            try
            {
                proc_info.Process = System.Diagnostics.Process.GetProcessById(pid);
            }
            catch
            {

            }

            return proc_info;
        }

        public bool SendSignal(Signum signal)
        {
            if (!PlatformSupportsSignaling)
                return false;

            Syscall.kill(Id, signal);
            return true;
        }

        public async System.Threading.Tasks.Task<bool> WaitForExitAsync(TimeSpan timeout)
        {
            if (HasExited)
                return true;

            var waiter = new ManualResetEvent(false);
            var cancellation_token = new CancellationTokenSource();

            OnProcessExit handler = null;
            handler = (OnProcessExit)((pid, code) => 
            {
                if (pid != this.Id)
                    return;

                cancellation_token.Cancel();
                ProcessHandler.ProcessExit -= handler;
            });

            ProcessHandler.ProcessExit += handler;
            if (HasExited)
            {
                handler(this.Id, 0);
                return true;
            }

            await System.Threading.Tasks.Task.Delay(timeout, cancellation_token.Token).ContinueWith(t => {});
            
            if (cancellation_token.IsCancellationRequested)
                return true;
            
            handler(this.Id, 0);
            return false;
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
