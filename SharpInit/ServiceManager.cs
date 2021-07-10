using SharpInit.Platform;
using SharpInit.Platform.Unix;
using SharpInit.Units;
using SharpInit.Tasks;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

using NLog;

namespace SharpInit
{
    public class ServiceManager
    {
        Logger Log = LogManager.GetCurrentClassLogger();

        public event OnUnitStateChanged UnitStateChanged;
        public event OnServiceProcessExit ServiceProcessExit;
        public event OnServiceProcessStart ServiceProcessStart;

        public List<ProcessInfo> ManagedProcesses = new List<ProcessInfo>();
        public Dictionary<int, ProcessInfo> ProcessesById = new Dictionary<int, ProcessInfo>();
        public Dictionary<Unit, List<ProcessInfo>> ProcessesByUnit = new Dictionary<Unit, List<ProcessInfo>>();

        public UnitRegistry Registry { get; set; }
        public SocketManager SocketManager { get; set; }
        public ISymlinkTools SymlinkTools { get; set; }
        public TaskRunner Runner { get; set; }
        public TransactionPlanner Planner { get; set; }

        public ScopeUnit Scope { get; set; }

        public Journal Journal;

        public IProcessHandler ProcessHandler;

        public CGroupManager CGroupManager { get; private set; }
        
        public ServiceManager() :
            this(PlatformUtilities.GetImplementation<IProcessHandler>())
        {
        }

        public ServiceManager(IProcessHandler process_handler)
        {
            ProcessHandler = process_handler;
            ProcessHandler.ProcessExit += HandleProcessExit;
            ProcessHandler.ServiceManager = this;
            
            SymlinkTools = PlatformUtilities.GetImplementation<ISymlinkTools>(this);

            Registry = new UnitRegistry(this);
            Registry.UnitAdded += HandleUnitAdded;
            Registry.CreateBaseUnits();

            Journal = new Journal();

            Planner = new TransactionPlanner(this);

            Runner = new TaskRunner(this);

            SocketManager = new SocketManager();

            if (PlatformUtilities.CurrentlyOn("linux"))
            {
                CGroupManager = new CGroupManager(this);
                InitializeCGroups();
            }
        }

        public void MoveToScope(string scope)
        {
            if (Scope != null)
                return;

            if (Registry.GetUnit(scope) == null)
            {
                var unit_file = new GeneratedUnitFile(scope, destroy_on_reload: false).WithProperty("Scope/Slice", "-.slice");
                Registry.IndexUnitFile(unit_file);
            }

            Scope = Registry.GetUnit<ScopeUnit>(scope);
            //Scope.ParentSlice = "-.slice";

            Runner.Register(Planner.CreateActivationTransaction(Scope)).Enqueue().Wait();

            var pids = CGroupManager.RootCGroup.ChildProcesses;

            foreach (var pid in pids)
                Scope.CGroup.Join(pid);
            
            CGroupManager.RootCGroup.Update();

            if (CGroupManager.RootCGroup.ChildProcesses.Any())
                throw new Exception($"Failed!");
        }

        private void InitializeCGroups()
        {
            if (!PlatformUtilities.CurrentlyOn("linux"))
                return;
            
            if (CGroupManager.CanCreateCGroups())
                return;

            CGroupManager.UpdateRoot();

            if (UnixPlatformInitialization.UnderSystemd)
            {
                if (UnixPlatformInitialization.IsPrivileged)
                {
                    Log.Info($"Running under systemd with root privileges. Allocating Delegate=yes cgroup...");

                    var sharpinitctl_location = "/usr/bin/sharpinitctl";
                    if (!File.Exists(sharpinitctl_location))
                    {
                        Log.Warn($"Could not find sharpinitctl at {sharpinitctl_location}. Please make it available at that path for automatic cgroup setup.");
                        Log.Warn($"To manually enable cgroup support for this SharpInit instance, run 'sharpinitctl join-manager-to-current-cgroup' in a Delegate=yes scope unit.");
                    }
                    else
                    {
                        // TODO: do this over dbus
                        var psi = new System.Diagnostics.ProcessStartInfo("/usr/bin/systemd-run");

                        psi.FileName = "/usr/bin/systemd-run";
                        psi.ArgumentList.Add("-p Delegate=yes");
                        psi.ArgumentList.Add("--scope");
                        psi.ArgumentList.Add(sharpinitctl_location); 
                        psi.ArgumentList.Add("join-manager-to-current-cgroup");
                        
                        System.Diagnostics.Process.Start(psi)?.WaitForExit();

                        if (CGroupManager.CanCreateCGroups())
                        {
                            Log.Info($"Successfully acquired cgroup from systemd: {CGroupManager.RootCGroup}");
                        }
                        else
                        {
                            Log.Warn($"Failed to acquire cgroup from systemd. cgroups will not be used.");
                        }
                    }
                }
                else
                {
                    if (!CGroupManager.CanCreateCGroups())
                    {
                        Log.Info($"Running under systemd, but SharpInit is unprivileged. Run 'sharpinitctl join-manager-to-current-cgroup' before starting any SharpInit units in a Delegate=yes systemd scope to enable cgroup support.");
                        Log.Info("After starting any SharpInit units, cgroup support cannot be enabled again.");
                    }
                    else
                    {
                        Log.Info($"cgroup support enabled, root cgroup is: {CGroupManager.RootCGroup}");
                    }
                }
            }
        }

        private void HandleUnitAdded(object sender, UnitAddedEventArgs e)
        {
            e.Unit.UnitStateChanged += PropagateStateChange;
        }

        private void PropagateStateChange(object sender, UnitStateChangedEventArgs e) => UnitStateChanged?.Invoke(sender, e);
        
        private void HandleProcessExit(int pid, int exit_code)
        {
            if (!ProcessesById.ContainsKey(pid)) 
            {
                return;
            }

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
