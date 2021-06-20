﻿using SharpInit.Platform;
using SharpInit.Units;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SharpInit
{
    public delegate void OnServiceProcessExit(Unit unit, ProcessInfo info, int code);
    public delegate void OnServiceProcessStart(Unit unit, ProcessInfo info);

    public class ServiceManager
    {
        public List<ProcessInfo> ManagedProcesses = new List<ProcessInfo>();
        public Dictionary<int, ProcessInfo> ProcessesById = new Dictionary<int, ProcessInfo>();
        public Dictionary<Unit, List<ProcessInfo>> ProcessesByUnit = new Dictionary<Unit, List<ProcessInfo>>();

        public Journal Journal;

        public IProcessHandler ProcessHandler;
        
        public ServiceManager() :
            this(PlatformUtilities.GetImplementation<IProcessHandler>())
        {
        }

        public ServiceManager(IProcessHandler process_handler)
        {
            Journal = new Journal();
            ProcessHandler = process_handler;
            ProcessHandler.ProcessExit += HandleProcessExit;
            ProcessHandler.ServiceManager = this;
        }
        
        private void HandleProcessExit(int pid, int exit_code)
        {
            var proc_info = ProcessesById[pid];
            var unit = proc_info.SourceUnit;

            unit.RaiseProcessExit(proc_info, exit_code);

            ManagedProcesses.Remove(proc_info);
            ProcessesById.Remove(pid);

            if (ProcessesByUnit.ContainsKey(unit))
                ProcessesByUnit[unit].Remove(proc_info);
        }

        public ProcessInfo StartProcess(Unit unit, ProcessStartInfo psi)
        {
            var process = ProcessHandler.Start(psi);

            if (!process.Process.HasExited)
                unit.RaiseProcessStart(process);

            RegisterProcess(unit, process);
            return process;
        }

        public void RegisterProcess(Unit unit, ProcessInfo proc)
        {
            if (ProcessesById.ContainsKey(proc.Id))
                throw new InvalidOperationException("This pid has already been registered");
            
            proc.ServiceManager = this;

            ManagedProcesses.Add(proc);
            ProcessesById[proc.Id] = proc;

            if (!ProcessesByUnit.ContainsKey(unit))
                ProcessesByUnit[unit] = new List<ProcessInfo>();

            ProcessesByUnit[unit].Add(proc);
            proc.SourceUnit = unit;
        }

        public void RegisterProcess(Unit unit, System.Diagnostics.Process proc)
        {
            RegisterProcess(unit, new ProcessInfo(proc, unit));
        }

        public void RegisterProcess(Unit unit, int pid)
        {
            RegisterProcess(unit, System.Diagnostics.Process.GetProcessById(pid));
        }
    }
}
