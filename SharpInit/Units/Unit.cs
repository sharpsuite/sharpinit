using SharpInit.Tasks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SharpInit.Units
{
    public delegate void OnUnitStateChange(Unit source, UnitState next_state);

    /// <summary>
    /// Base unit class with shared functionality that all unit types must inherit from.
    /// </summary>
    public abstract class Unit
    {
        public string UnitName { get; set; }
        public UnitState CurrentState { get; internal set; }

        public UnitFile File { get => GetUnitFile(); }

        public ServiceManager ServiceManager { get; set; }

        public event OnUnitStateChange UnitStateChange;
        public event OnProcessStart ProcessStart;
        public event OnProcessExit ProcessExit;

        public DateTime LastStateChangeTime { get; set; }
        public DateTime ActivationTime { get; set; }
        public DateTime LoadTime { get; set; }

        private DependencyGraph<RequirementDependency> RequirementDependencyGraph { get; set; }
        private DependencyGraph<OrderingDependency> OrderingDependencyGraph { get; set; }
        
        protected Unit(string path)
        {
            LoadUnitFile(path);
            UnitName = File.UnitName;
        }

        protected Unit()
        {

        }

        public abstract UnitFile GetUnitFile();
        public abstract void LoadUnitFile(string path);
        
        internal void SetState(UnitState next_state)
        {
            UnitStateChange?.Invoke(this, next_state); // block while state changes are handled
                                                       // TODO: Investigate whether this could result in a deadlock

            CurrentState = next_state;
            LastStateChangeTime = DateTime.UtcNow;
        }

        // Use the CreateActivationTransaction/CreateDeactivationTransaction in UnitRegistry, not these.
        internal abstract Transaction GetActivationTransaction();
        internal abstract Transaction GetDeactivationTransaction();
        public abstract Transaction GetReloadTransaction();
        
        public void ReloadUnitFile()
        {
            LoadUnitFile(File.UnitPath);
            UnitName = File.UnitName;
        }

        internal void RaiseProcessExit(ProcessInfo proc, int exit_code)
        {
            ProcessExit?.Invoke(this, proc, exit_code);
        }

        internal void RaiseProcessStart(ProcessInfo proc)
        {
            ProcessStart?.Invoke(this, proc);
        }

        public void RegisterDependencies(DependencyGraph<OrderingDependency> ordering_graph, DependencyGraph<RequirementDependency> requirement_graph)
        {
            // first, clean up after ourselves
            if(OrderingDependencyGraph != null)
            {
                var dependencies_from_us = OrderingDependencyGraph.Dependencies.Where(dep => dep.SourceUnit == UnitName);

                foreach (var dep in dependencies_from_us)
                    OrderingDependencyGraph.Dependencies.Remove(dep);
            }

            if (RequirementDependencyGraph != null)
            {
                var dependencies_from_us = RequirementDependencyGraph.Dependencies.Where(dep => dep.SourceUnit == UnitName);

                foreach (var dep in dependencies_from_us)
                    RequirementDependencyGraph.Dependencies.Remove(dep);
            }

            // set new graphs
            OrderingDependencyGraph = ordering_graph;
            RequirementDependencyGraph = requirement_graph;

            AddDependencies();
        }

        private void AddDependencies()
        {
            if(OrderingDependencyGraph != null)
            {
                var after_deps = File.After.Select(after => new OrderingDependency(UnitName, after, UnitName, OrderingDependencyType.After));
                var before_deps = File.Before.Select(before => new OrderingDependency(before, UnitName, UnitName, OrderingDependencyType.After));

                OrderingDependencyGraph.AddDependencies(after_deps, before_deps);
            }

            if(RequirementDependencyGraph != null)
            {
                var requires_deps = File.Requires.Select(requires => new RequirementDependency(UnitName, requires, UnitName, RequirementDependencyType.Requires));
                var requisite_deps = File.Requisite.Select(requisite => new RequirementDependency(UnitName, requisite, UnitName, RequirementDependencyType.Requisite));
                var wants_deps = File.Wants.Select(wants => new RequirementDependency(UnitName, wants, UnitName, RequirementDependencyType.Wants));
                var binds_to_deps = File.BindsTo.Select(bind => new RequirementDependency(UnitName, bind, UnitName, RequirementDependencyType.BindsTo));
                var part_of_deps = File.PartOf.Select(part_of => new RequirementDependency(UnitName, part_of, UnitName, RequirementDependencyType.PartOf));
                var conflicts_deps = File.Conflicts.Select(conflict => new RequirementDependency(UnitName, conflict, UnitName, RequirementDependencyType.Conflicts));

                RequirementDependencyGraph.AddDependencies(requires_deps, requisite_deps, wants_deps, binds_to_deps, part_of_deps, conflicts_deps);
            }
        }

        public override string ToString()
        {
            return $"[Unit Type={this.GetType().Name}, Name={UnitName}, State={CurrentState}, Path={File.UnitPath}]";
        }
    }

    public enum UnitState
    {
        Inactive,
        Active,
        Activating,
        Deactivating,
        Failed,
        Reloading,
        Any // used as a special mask
    }
}
