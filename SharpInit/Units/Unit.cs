﻿using SharpInit.Tasks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SharpInit.Units
{
    /// <summary>
    /// Base unit class with shared functionality that all unit types must inherit from.
    /// </summary>
    public abstract class Unit
    {
        public string UnitName { get; set; }

        public UnitState PreviousState { get; internal set; }
        public UnitState CurrentState { get; internal set; }

        public string StateChangeReason { get; internal set; }

        public UnitDescriptor Descriptor { get => GetUnitDescriptor(); set => SetUnitDescriptor(value); }

        public SocketManager SocketManager { get; set; }

        public ServiceManager ServiceManager { get; set; }

        public event OnUnitStateChange UnitStateChange;
        public event OnServiceProcessStart ProcessStart;
        public event OnServiceProcessExit ProcessExit;

        public DateTime LastStateChangeTime { get; set; }
        public DateTime ActivationTime { get; set; }
        public DateTime LoadTime { get; set; }

        private DependencyGraph<RequirementDependency> RequirementDependencyGraph { get; set; }
        private DependencyGraph<OrderingDependency> OrderingDependencyGraph { get; set; }

        protected Unit(string name, UnitDescriptor descriptor)
            : this()
        {
            UnitName = name;
            Descriptor = descriptor;
        }

        protected Unit()
        {
            PreviousState = UnitState.Inactive;
            CurrentState = UnitState.Inactive;
        }

        public abstract UnitDescriptor GetUnitDescriptor();
        public abstract void SetUnitDescriptor(UnitDescriptor desc);
        //public abstract void LoadUnitFile(string path);
        //public abstract void LoadUnitFile(UnitFile file);

        internal void SetState(UnitState next_state, string reason = null)
        {
            // block while state changes are handled
            // TODO: Investigate whether this could result in a deadlock
            UnitStateChange?.Invoke(this, new UnitStateChangeEventArgs(this, next_state, reason)); 

            PreviousState = CurrentState;
            CurrentState = next_state;
            LastStateChangeTime = DateTime.UtcNow;

            if (reason != null)
                StateChangeReason = reason;
        }

        // Use the CreateActivationTransaction/CreateDeactivationTransaction in UnitRegistry, not these.
        internal abstract Transaction GetActivationTransaction();
        internal abstract Transaction GetDeactivationTransaction();
        public abstract Transaction GetReloadTransaction();
        
        //public void ReloadUnitFile()
        //{
        //    LoadUnitFile(File.UnitPath);
        //    UnitName = File.UnitName;
        //}

        public void ReloadUnitDescriptor()
        {
            var new_descriptor = UnitParser.FromFiles(Descriptor.GetType(), Descriptor.Files.Select(file => file is OnDiskUnitFile ? UnitParser.ParseFile((file as OnDiskUnitFile).Path) : file).ToArray());
            Descriptor = new_descriptor;
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
                OrderingDependencyGraph.Dependencies.RemoveAll(dep => dep.SourceUnit == UnitName);
            }

            if (RequirementDependencyGraph != null)
            {
                RequirementDependencyGraph.Dependencies.RemoveAll(dep => dep.SourceUnit == UnitName);
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
                var after_deps = Descriptor.After.Select(after => new OrderingDependency(UnitName, after, UnitName, OrderingDependencyType.After));
                var before_deps = Descriptor.Before.Select(before => new OrderingDependency(before, UnitName, UnitName, OrderingDependencyType.After));

                OrderingDependencyGraph.AddDependencies(after_deps, before_deps);
            }

            if(RequirementDependencyGraph != null)
            {
                var requires_deps = Descriptor.Requires.Select(requires => new RequirementDependency(UnitName, requires, UnitName, RequirementDependencyType.Requires));
                var requisite_deps = Descriptor.Requisite.Select(requisite => new RequirementDependency(UnitName, requisite, UnitName, RequirementDependencyType.Requisite));
                var wants_deps = Descriptor.Wants.Select(wants => new RequirementDependency(UnitName, wants, UnitName, RequirementDependencyType.Wants));
                var binds_to_deps = Descriptor.BindsTo.Select(bind => new RequirementDependency(UnitName, bind, UnitName, RequirementDependencyType.BindsTo));
                var part_of_deps = Descriptor.PartOf.Select(part_of => new RequirementDependency(UnitName, part_of, UnitName, RequirementDependencyType.PartOf));
                var conflicts_deps = Descriptor.Conflicts.Select(conflict => new RequirementDependency(UnitName, conflict, UnitName, RequirementDependencyType.Conflicts));

                RequirementDependencyGraph.AddDependencies(requires_deps, requisite_deps, wants_deps, binds_to_deps, part_of_deps, conflicts_deps);
            }
        }

        public override string ToString()
        {
            return $"[Unit Type={this.GetType().Name}, Name={UnitName}, State={CurrentState}, Paths=[{string.Join(", ", Descriptor.Files.Select(file => file.ToString()))}]]";
        }
    }

    public enum UnitState
    {
        Inactive = 0x1,
        Active = 0x2,
        Activating = 0x4,
        Deactivating = 0x8,
        Failed = 0x10,
        Reloading = 0x20,
        Any = 0x7FFFFFFF
    }
}
