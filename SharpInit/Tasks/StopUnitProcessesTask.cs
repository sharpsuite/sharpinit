using SharpInit.Units;
using System;
using System.Collections.Generic;
using System.Text;
using NLog;
using System.Linq;

namespace SharpInit.Tasks
{
    /// <summary>
    /// Stops all processes registered under a unit.
    /// </summary>
    public class StopUnitProcessesTask : AsyncTask
    {
        Logger Log = LogManager.GetCurrentClassLogger();
        public override string Type => "stop-processes-by-unit";
        public Unit Unit { get; set; }
        public int GracePeriod { get; set; }

        /// <summary>
        /// Stops all processes registered under a unit.
        /// </summary>
        /// <param name="unit">The unit that owns the processes.</param>
        /// <param name="grace_period">On supported platforms, this is the gap (in milliseconds) between sending a SIGTERM and a SIGKILL.</param>
        public StopUnitProcessesTask(Unit unit, int grace_period = 5000)
        {
            Unit = unit;
            GracePeriod = grace_period;
        }

        public async override System.Threading.Tasks.Task<TaskResult> ExecuteAsync(TaskContext context)
        {
            var service_manager = Unit.ServiceManager;

            if (!service_manager.ProcessesByUnit.ContainsKey(Unit))
                return new TaskResult(this, ResultType.Success);

            bool failed = false;
            
            var processes = new ProcessInfo[0];
            var kill_immediate = new Dictionary<int, bool>();
            var exec = (Unit.Descriptor is ExecUnitDescriptor) ? (ExecUnitDescriptor)Unit.Descriptor : null;
            var kill_mode = (exec != null) ? exec.KillMode : KillMode.ControlGroup;
            var initial_kill = (exec != null) ? exec.KillSignal : 15;
            var hard_kill = (exec != null) ? exec.FinalKillSignal : 9;
            var after_kill = (exec != null) ? exec.SendSighup ? 1 : -1 : -1;

            if (exec != null && !exec.SendSigkill)
                hard_kill = -1;

            if (kill_mode == KillMode.Process)
            {
                if (Unit.MainProcessId == default)
                {
                    Log.Warn($"Kill mode is set to process, yet unit {Unit.UnitName} has no main process id. Assuming control-group instead.");
                    kill_mode = KillMode.ControlGroup;
                }
            }

            switch (kill_mode)
            {
                case KillMode.ControlGroup:
                case KillMode.Mixed:
                    if (Unit.CGroup != null)
                    {
                        Unit.CGroup.Update();

                        if (!Unit.CGroup.ChildProcesses.Any())
                        {
                            Log.Warn($"Unit {Unit.UnitName} has kill mode control-group, yet no processes found in its cgroup {Unit.CGroup}. Trying its registered processes instead...");
                            processes = service_manager.ProcessesByUnit[Unit].ToArray();
                        }
                        else
                        {
                            var process_list = new List<ProcessInfo>();

                            foreach (var pid in Unit.CGroup.ChildProcesses)
                            {
                                if (service_manager.ProcessesById.ContainsKey(pid))
                                    process_list.Add(service_manager.ProcessesById[pid]);
                                else
                                    process_list.Add(ProcessInfo.FromPid(pid));
                            }

                            processes = process_list.ToArray();
                        }
                    }
                    else
                    {
                        Log.Warn($"Unit {Unit.UnitName} has kill mode control-group, but has no cgroup. Using registered processes instead.");
                        processes = service_manager.ProcessesByUnit[Unit].ToArray();
                    }

                    if (kill_mode == KillMode.Mixed)
                    {
                        foreach (var proc in processes)
                        {
                            if (proc.Id != Unit.MainProcessId)
                                kill_immediate[proc.Id] = true;
                        }
                    }
                    break;
                case KillMode.None:
                    break;
                default:
                case KillMode.Process:
                    processes = new [] { service_manager.ProcessesById[Unit.MainProcessId] };
                    break;
            }

            var nonimmediate_kills = processes.Where(p => !kill_immediate.ContainsKey(p.Id) || !kill_immediate[p.Id]).ToArray();
            var immediate_kills = processes.Where(p => kill_immediate.ContainsKey(p.Id) && kill_immediate[p.Id]).ToArray();

            foreach (var process in nonimmediate_kills)
            {
                var stop_process_task = new StopProcessTask(process, kill: initial_kill, final_kill: hard_kill, after_kill: after_kill, grace_period: GracePeriod);
                var result = await ExecuteAsync(stop_process_task, context);

                if (result.Type != ResultType.Success)
                    failed = true; // let's keep killing all processes we can anyway 
            }

            foreach (var process in immediate_kills)
            {
                var stop_process_task = new StopProcessTask(process, kill: hard_kill, grace_period: 1);
                var result = await ExecuteAsync(stop_process_task, context);

                if (result.Type != ResultType.Success)
                    failed = true; // let's keep killing all processes we can anyway 
            }

            return new TaskResult(this, failed ? ResultType.SoftFailure : ResultType.Success);
        }
    }
}
