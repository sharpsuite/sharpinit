using SharpInit.Platform;
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

        public IProcessHandler ProcessHandler;
        
        public ServiceManager()
        {

        }
        
        private void HandleProcessExit(object sender, EventArgs e)
        {
            var proc = (Process)sender;
            var pid = proc.Id;

            var proc_info = ProcessesById[pid];
            var unit = proc_info.SourceUnit;

            unit.RaiseProcessExit(proc_info, proc.ExitCode);

            ManagedProcesses.Remove(proc_info);
            ProcessesById.Remove(pid);

            if (ProcessesByUnit.ContainsKey(unit))
                ProcessesByUnit[unit].Remove(proc_info);
        }

        public void StartProcess(Unit unit, ProcessStartInfo psi)
        {
            var process = Process.Start(psi);

            var proc_info = new ProcessInfo(process, unit);

            if (!process.HasExited)
                unit.RaiseProcessStart(proc_info);

            RegisterProcess(unit, proc_info);
        }

        public void RegisterProcess(Unit unit, ProcessInfo proc)
        {
            if (ProcessesById.ContainsKey(proc.Id))
                throw new InvalidOperationException("This pid has already been registered");

            ManagedProcesses.Add(proc);
            ProcessesById[proc.Id] = proc;

            if (!ProcessesByUnit.ContainsKey(unit))
                ProcessesByUnit[unit] = new List<ProcessInfo>();

            ProcessesByUnit[unit].Add(proc);

            proc.Process.EnableRaisingEvents = true;
            proc.Process.Exited += HandleProcessExit;
            proc.Process.StandardOutput.Close(); // TODO: redirect stdout to a log file/system
            proc.Process.StandardError.Close();  // we close these right now because child processes can hang on stdout/stderr IO
        }

        public void RegisterProcess(Unit unit, Process proc)
        {
            RegisterProcess(unit, new ProcessInfo(proc, unit));
        }

        public void RegisterProcess(Unit unit, int pid)
        {
            RegisterProcess(unit, Process.GetProcessById(pid));
        }
    }
}
