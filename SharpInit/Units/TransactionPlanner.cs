using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;

using SharpInit.Tasks;

using NLog;

namespace SharpInit.Units
{
    public class TransactionPlanner
    {
        Logger Log = LogManager.GetCurrentClassLogger();
        ServiceManager ServiceManager { get; set; }
        UnitRegistry Registry => ServiceManager.Registry;

        public TransactionPlanner(ServiceManager manager)
        {
            ServiceManager = manager;
        }

        public UnitStateChangeTransaction CreateActivationTransaction(string name, string reason = null) => CreateActivationTransaction(Registry.GetUnit(name), reason);
        

        public UnitStateChangeTransaction CreateActivationTransaction(Unit unit, string reason = null)
        {
            var transaction = new UnitStateChangeTransaction(unit, UnitStateChangeType.Activation);

            transaction.TransactionSynchronizationMode = TransactionSynchronizationMode.Explicit;

            if (reason != null)
                transaction.Add(new AlterTransactionContextTask("state_change_reason", reason));

            var unit_list = new List<Unit>() { unit };

            var ignore_conflict_deactivation_failure = new Dictionary<string, bool>();
            var fail_if_unstarted = new Dictionary<string, bool>();
            var ignore_failure = new Dictionary<string, bool>() { { unit.UnitName, false } };

            var copy_of_req_deps = Registry.RequirementDependencies.Clone();
            var copy_of_ord_deps = Registry.OrderingDependencies.Clone();

            var visited_dependencies = new List<Dependency>();

            IEnumerable<RequirementDependency> req_graph = null;
            
            while ((req_graph = copy_of_req_deps.TraverseDependencyGraph(
                unit.UnitName, 
                t => t.RequirementType != RequirementDependencyType.Conflicts && t.RequirementType != RequirementDependencyType.PartOf, 
                add_reverse: false)).Count() != visited_dependencies.Count)
            {
                // list all units to be started
                foreach(var dependency in req_graph)
                {
                    if (visited_dependencies.Contains(dependency))
                        continue;
                    
                    visited_dependencies.Add(dependency);

                    var parent = dependency.LeftUnit;
                    var child = dependency.RightUnit;

                    var target_unit = Registry.GetUnit(child);

                    if (target_unit == null)
                        continue;

                    if (!unit_list.Contains(target_unit))
                        unit_list.Add(target_unit);
                    
                    if (!transaction.Reasoning.ContainsKey(target_unit))
                    {
                        transaction.Reasoning[target_unit] = new List<string>();
                    }

                    transaction.Reasoning[target_unit].Add($"Activating {target_unit.UnitName} because of dependency {dependency}");
                }

                copy_of_req_deps = Registry.RequirementDependencies.Clone();
            }

            // determine whether the failure of each unit activation makes the entire transaction fail
            string current_unit = unit.UnitName;
            var list = new List<RequirementDependency>();

            while (true)
            {
                var dependencies_to_resolve = req_graph.Where(dep => dep.LeftUnit == current_unit).ToList();

                foreach (var dependency in dependencies_to_resolve)
                {
                    if(dependency.RequirementType == RequirementDependencyType.Wants)
                    {
                        ignore_failure[dependency.RightUnit] = ignore_failure.ContainsKey(dependency.RightUnit) ? ignore_failure[dependency.RightUnit] : true;
                    }
                    else
                    {
                        ignore_failure[dependency.RightUnit] = false;
                        list.Add(dependency);
                    }
                }

                if (!list.Any())
                    break;

                current_unit = list.First().RightUnit;
                list.RemoveAt(0);
            }

            // determine whether each unit is actually to be started or not (Requisite only checks whether the unit is active)
            fail_if_unstarted = unit_list.ToDictionary(u => u.UnitName, u => copy_of_req_deps.GetDependencies(u.UnitName).All(dep => dep.RequirementType == RequirementDependencyType.Requisite));
            fail_if_unstarted[unit.UnitName] = false; // the unit we're set out to start isn't subject to this

            // create unit ordering according to ordering dependencies
            var order_graph = copy_of_ord_deps.TraverseDependencyGraph(unit.UnitName, t => true, true).ToList();
            
            var new_order = new List<Unit>();
            var initial_nodes = order_graph.Where(dependency => !order_graph.Any(d => dependency.LeftUnit == d.RightUnit));
            var initial_nodes_filtered = initial_nodes.Where(dependency => unit_list.Any(u => dependency.LeftUnit == u.UnitName || dependency.RightUnit == u.UnitName));
            var selected_nodes = new List<List<string>>() { initial_nodes_filtered.Select(t => t.LeftUnit).Distinct().ToList() }; // find the "first" nodes

            if (!initial_nodes_filtered.Any() && !initial_nodes.Any() && order_graph.Any())
            {
                // possible dependency loop
                throw new Exception($"Failed to order dependent units while preparing the activation transaction for {unit.UnitName}.");
            }
            else if (!initial_nodes_filtered.Any())
                new_order = unit_list;
            else
            {
                var processed_vertices = new List<string>();

                while(selected_nodes.Any())
                {
                    var node_list = selected_nodes[0];
                    selected_nodes.RemoveAt(0);

                    var edges_to_add = new List<string>();

                    foreach (var dep in node_list)
                    {
                        processed_vertices.Add(dep);
                        var dep_unit = Registry.GetUnit(dep);

                        if (dep_unit == null)
                        {
                            if (!ignore_failure.ContainsKey(dep) || ignore_failure[dep])
                                continue;
                            else
                                throw new Exception($"Couldn't find required unit {dep}");
                        }
                        
                        if (unit_list.Contains(dep_unit) && !new_order.Contains(dep_unit))
                        {
                            new_order.Add(dep_unit);
                        }

                        var other_edges = order_graph.Where(d => d.LeftUnit == dep).ToList();
                        other_edges.ForEach(edge => order_graph.Remove(edge));
                        edges_to_add.AddRange(other_edges.Where(edge => { var m = edge.RightUnit; return !order_graph.Any(e => e.RightUnit == m && !processed_vertices.Contains(e.LeftUnit)); }).Select(t => t.RightUnit));

                    }

                    new_order.Add(null);
                    edges_to_add = edges_to_add.Distinct().ToList();

                    if (edges_to_add.Any())
                        selected_nodes.Add(edges_to_add);
                }

                new_order = new_order.Concat(unit_list.Where(u => !new_order.Contains(u)).ToList()).ToList();
                new_order.Reverse();

                // check the new order against the rules
                bool satisfied = true;
                var breaking = new List<OrderingDependency>();

                foreach (var order_rule in order_graph)
                {
                    var index_1 = new_order.FindIndex(u => u?.UnitName == order_rule.LeftUnit);
                    var index_2 = new_order.FindIndex(u => u?.UnitName == order_rule.RightUnit);

                    if (index_1 == -1 || index_2 == -1)
                        continue;

                    if (index_1 < index_2)
                    {
                        if (Registry.GetUnit(order_rule.LeftUnit) == null)
                        {
                            Log.Warn($"Ignoring ordering dependency against missing unit {order_rule.LeftUnit}");
                            continue;
                        }

                        satisfied = false;
                        breaking.Add(order_rule);
                        break;
                    }
                }

                if (!satisfied)
                    throw new Exception($"Unsatisfiable set of ordering rules encountered when building the activation transaction for unit {unit.UnitName}.");
            }

            unit_list = new_order;

            // get a list of units to stop
            var conflicts = unit_list.Where(u => u != null).SelectMany(u => copy_of_req_deps.GetDependencies(u.UnitName).Where(d => d.RequirementType == RequirementDependencyType.Conflicts));
            var units_to_stop = new List<Unit>();

            foreach(var conflicting_dep in conflicts)
            {
                var left = Registry.GetUnit(conflicting_dep.LeftUnit);
                var right = Registry.GetUnit(conflicting_dep.RightUnit);

                if (left == null || right == null)
                    continue;

                Unit unit_to_stop = null;

                if (unit_list.Contains(left) && unit_list.Contains(right)) // conflict inside transaction
                {
                    var left_ignorable = !ignore_failure.ContainsKey(left.UnitName) || ignore_failure[left.UnitName];
                    var right_ignorable = !ignore_failure.ContainsKey(right.UnitName) || ignore_failure[right.UnitName];

                    if (!left_ignorable && !right_ignorable)
                        throw new Exception($"Units {left.UnitName} and {right.UnitName} conflict because of dependency {conflicting_dep}");

                    if (left_ignorable && !units_to_stop.Contains(left))
                    {
                        unit_to_stop = left;
                    }

                    if (right_ignorable && !units_to_stop.Contains(right))
                    {
                        unit_to_stop = right;
                    }
                }
                else if (unit_list.Contains(left) && !units_to_stop.Contains(right))
                {
                    unit_to_stop = right;
                }
                else if (unit_list.Contains(right) && !units_to_stop.Contains(left))
                {
                    unit_to_stop = left;
                }
                else
                {
                    continue;
                }

                if (unit_to_stop == null)
                    continue;

                units_to_stop.Add(unit_to_stop);

                if (!transaction.Reasoning.ContainsKey(unit_to_stop))
                    transaction.Reasoning[unit_to_stop] = new List<string>();

                transaction.Reasoning[unit_to_stop].Add($"Deactivating {unit_to_stop.UnitName} because of dependency {conflicting_dep}");
            }

            ignore_conflict_deactivation_failure = units_to_stop.ToDictionary(u => u.UnitName,
                u => conflicts.All(conflict =>
                {
                    var requesting_end = u.UnitName == conflict.LeftUnit ? conflict.RightUnit : conflict.LeftUnit;
                    return !ignore_failure.ContainsKey(requesting_end) || ignore_failure[requesting_end];
                }));

            // actually create the transaction

            foreach (var sub_unit in units_to_stop)
            {
                transaction.AffectedUnits.Add(sub_unit);
                var deactivation_transaction = CreateDeactivationTransaction(sub_unit, $"{unit.UnitName} is being activated");

                deactivation_transaction.Prepend(new CheckUnitStateTask(UnitState.Active, sub_unit.UnitName, true));
                deactivation_transaction.ErrorHandlingMode = ignore_conflict_deactivation_failure[sub_unit.UnitName] ? TransactionErrorHandlingMode.Ignore : TransactionErrorHandlingMode.Fail;

                transaction.Add(deactivation_transaction);
                transaction.Add(new SynchronizationTask());
            }

            foreach(var sub_unit in unit_list)
            {
                if (sub_unit == null)
                {
                    transaction.Add(new SynchronizationTask());
                    continue;
                }

                transaction.AffectedUnits.Add(sub_unit);
                var activation_transaction = sub_unit.GetActivationTransaction();

                if (fail_if_unstarted.ContainsKey(sub_unit.UnitName) && fail_if_unstarted[sub_unit.UnitName])
                    activation_transaction.Prepend(new CheckUnitStateTask(UnitState.Active, sub_unit.UnitName));

                if (ignore_failure.ContainsKey(sub_unit.UnitName) && !ignore_failure[sub_unit.UnitName])
                    activation_transaction.ErrorHandlingMode = TransactionErrorHandlingMode.Fail;
                else
                    activation_transaction.ErrorHandlingMode = TransactionErrorHandlingMode.Ignore;

                transaction.Add(activation_transaction);
            }

            return transaction;
        }

        public UnitStateChangeTransaction CreateDeactivationTransaction(string unit, string reason = null) => CreateDeactivationTransaction(Registry.GetUnit(unit), reason);
        
        public UnitStateChangeTransaction CreateDeactivationTransaction(Unit unit, string reason = null)
        {
            var transaction = new UnitStateChangeTransaction(unit, UnitStateChangeType.Deactivation);

            transaction.TransactionSynchronizationMode = TransactionSynchronizationMode.Explicit;

            if (reason != null)
                transaction.Add(new AlterTransactionContextTask("state_change_reason", reason));

            var copy_of_req_deps = Registry.RequirementDependencies.Clone();
            var copy_of_ord_deps = Registry.OrderingDependencies.Clone();

            var units_to_deactivate = copy_of_req_deps.TraverseDependencyGraph(unit.UnitName, 
                t => t.RequirementType == RequirementDependencyType.BindsTo || 
                t.RequirementType == RequirementDependencyType.PartOf).SelectMany(dep => 
                {
                    var ret = new[] { dep.LeftUnit, dep.RightUnit };

                    if(dep.RequirementType == RequirementDependencyType.PartOf) // PartOf is a one way dependency
                    {
                        if (unit.UnitName != dep.RightUnit) // only when stopping the "right hand" side of a PartOf dependency should the action be propagated
                            return new string[0];
                    }

                    foreach (var unit_name in ret)
                    {
                        var u = Registry.GetUnit(unit_name);

                        if (u == null)
                            continue;

                        if (!transaction.Reasoning.ContainsKey(u))
                        {
                            transaction.Reasoning[u] = new List<string>();
                        }

                        transaction.Reasoning[u].Add($"Deactivating {u.UnitName} because of dependency {dep}");
                    }

                    return ret;
                })
                .Select(Registry.GetUnit).ToList();

            units_to_deactivate.Add(unit);
            units_to_deactivate = units_to_deactivate.Distinct().ToList();

            var order_graph = copy_of_ord_deps.TraverseDependencyGraph(unit.UnitName, t => units_to_deactivate.Any(u => u.UnitName == t.LeftUnit || u.UnitName == t.RightUnit), true).ToList();

            var new_order = new List<Unit>();
            var initial_nodes = order_graph.Where(dependency => !order_graph.Any(d => dependency.LeftUnit == d.RightUnit)).Select(t => t.LeftUnit).Distinct().ToList(); // find the "first" nodes

            if (!initial_nodes.Any() && order_graph.Any())
            {
                // possible dependency loop
                throw new Exception($"Failed to order dependent units while preparing the deactivation transaction for {unit.UnitName}.");
            }
            else if (!initial_nodes.Any())
            {
                new_order = units_to_deactivate;
            }
            else
            {
                var processed_vertices = new List<string>();

                while (initial_nodes.Any())
                {
                    var dep = initial_nodes.First();
                    initial_nodes.Remove(dep);
                    new_order.Add(Registry.GetUnit(dep));

                    var other_edges = order_graph.Where(d => d.LeftUnit == dep).ToList();
                    other_edges.ForEach(edge => order_graph.Remove(edge));
                    var edges_to_add = other_edges.Where(edge => { var m = edge.RightUnit; return !order_graph.Any(e => e.RightUnit == m && !processed_vertices.Contains(e.LeftUnit)); }).Select(t => t.RightUnit);

                    initial_nodes.AddRange(edges_to_add);
                }

                // prune the new order down to only units we've decided to deactivate, then append the unordered units not included in the new order
                new_order = new_order.Where(units_to_deactivate.Contains).Concat(units_to_deactivate.Where(u => !new_order.Contains(u)).ToList()).ToList();

                // check the new order against the rules
                bool satisfied = true;

                foreach (var order_rule in order_graph)
                {
                    var index_1 = new_order.FindIndex(u => u.UnitName == order_rule.LeftUnit);
                    var index_2 = new_order.FindIndex(u => u.UnitName == order_rule.RightUnit);

                    if (index_1 == -1 || index_2 == -1) // one of the vertices got pruned
                        continue;

                    if (index_1 > index_2) // now in reverse
                    {
                        satisfied = false;
                        break;
                    }
                }

                if (!satisfied)
                    throw new Exception($"Unsatisfiable set of ordering rules encountered when building the deactivation transaction for unit {unit.UnitName}.");
            }

            units_to_deactivate = new_order;

            // build the transaction

            foreach(var sub_unit in units_to_deactivate)
            {
                transaction.AffectedUnits.Add(sub_unit);
                transaction.Add(sub_unit.GetDeactivationTransaction());
                transaction.Add(new SynchronizationTask());
            }

            return transaction;
        }
    }
}