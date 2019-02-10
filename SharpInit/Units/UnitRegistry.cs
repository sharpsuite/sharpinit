using SharpInit.Tasks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SharpInit.Units
{
    public static class UnitRegistry
    {
        public static Dictionary<string, Unit> Units = new Dictionary<string, Unit>();
        public static Dictionary<string, Type> UnitTypes = new Dictionary<string, Type>();

        public static DependencyGraph<OrderingDependency> OrderingDependencies = new DependencyGraph<OrderingDependency>();
        public static DependencyGraph<RequirementDependency> RequirementDependencies = new DependencyGraph<RequirementDependency>();

        public static void AddUnit(Unit unit)
        {
            if (unit == null)
                return;

            if (Units.ContainsKey(unit.UnitName))
                throw new InvalidOperationException();

            unit.RegisterDependencies(OrderingDependencies, RequirementDependencies);
            Units[unit.UnitName] = unit;
        }

        public static void AddUnitByPath(string path) => AddUnit(CreateUnit(path));

        public static void ScanDirectory(string path, bool recursive = false)
        {
            var directories = recursive ? Directory.GetDirectories(path) : new string[0];
            var files = Directory.GetFiles(path);

            foreach (var file in files)
                AddUnitByPath(file);

            foreach (var dir in directories)
                ScanDirectory(dir, recursive);
        }

        public static Unit GetUnit(string name) => Units.ContainsKey(name) ? Units[name] : null;

        public static Unit CreateUnit(string path)
        {
            var ext = Path.GetExtension(path);

            if (!UnitTypes.ContainsKey(ext))
                return null;

            return (Unit)Activator.CreateInstance(UnitTypes[ext], path);
        }

        public static void InitializeTypes()
        {
            UnitTypes[".unit"] = typeof(Unit);
            UnitTypes[".service"] = typeof(ServiceUnit);
        }

        public static Transaction CreateActivationTransaction(string name)
        {
            return CreateActivationTransaction(GetUnit(name));
        }

        public static Transaction CreateActivationTransaction(Unit unit)
        {
            var unit_list = new List<Unit>() { unit };

            var ignore_failure = new Dictionary<string, bool>() { { unit.UnitName, false } };
            var req_graph = RequirementDependencies.TraverseDependencyGraph(unit.UnitName, t => t.RequirementType != RequirementDependencyType.Conflicts, false);
            
            // list all units to be started
            foreach(var dependency in req_graph)
            {
                var parent = dependency.LeftUnit;
                var child = dependency.RightUnit;

                var target_unit = GetUnit(child);

                if(!unit_list.Contains(target_unit))
                    unit_list.Add(target_unit);
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

            // create unit ordering according to ordering dependencies
            var order_graph = OrderingDependencies.TraverseDependencyGraph(unit.UnitName, t => true, true).ToList();
            
            var new_order = new List<Unit>();
            var initial_nodes = order_graph.Where(dependency => !order_graph.Any(d => dependency.LeftUnit == d.RightUnit)).Select(t => t.LeftUnit).ToList(); // find the "first" nodes

            if (!initial_nodes.Any() && order_graph.Any())
            {
                // possible dependency loop
                throw new Exception($"Failed to order dependent units while preparing the activation transaction for {unit.UnitName}.");
            }
            else if (!initial_nodes.Any() && order_graph.Any())
                new_order = unit_list;
            else
            {
                var processed_vertices = new List<string>();

                while(initial_nodes.Any())
                {
                    var dep = initial_nodes.First();
                    initial_nodes.Remove(dep);
                    new_order.Add(GetUnit(dep));

                    var other_edges = order_graph.Where(d => d.LeftUnit == dep).ToList();
                    other_edges.ForEach(edge => order_graph.Remove(edge));
                    var edges_to_add = other_edges.Where(edge => { var m = edge.RightUnit; return !order_graph.Any(e => e.RightUnit == m && !processed_vertices.Contains(e.LeftUnit)); }).Select(t => t.RightUnit);

                    initial_nodes.AddRange(edges_to_add);
                }

                new_order.Reverse();
                new_order = new_order.Concat(unit_list.Where(u => !new_order.Contains(u)).ToList()).ToList();

                // check the new order against the rules
                var order_deps = OrderingDependencies.TraverseDependencyGraph(unit.UnitName, t => true);
                bool satisfied = true;

                foreach (var order_rule in order_deps)
                {
                    var index_1 = new_order.FindIndex(u => u.UnitName == order_rule.LeftUnit);
                    var index_2 = new_order.FindIndex(u => u.UnitName == order_rule.RightUnit);

                    if (index_1 < index_2)
                    {
                        satisfied = false;
                        break;
                    }
                }

                if (!satisfied)
                    throw new Exception($"Unsatisfiable set of ordering rules encountered when building the activation transaction for unit {unit.UnitName}.");
            }

            unit_list = new_order;

            // actually create the transaction
            var transaction = new Transaction();

            foreach(var sub_unit in unit_list)
            {
                var activation_transaction = sub_unit.GetActivationTransaction();

                if (ignore_failure.ContainsKey(sub_unit.UnitName) && !ignore_failure[sub_unit.UnitName])
                    activation_transaction.ErrorHandlingMode = TransactionErrorHandlingMode.Fail;
                else
                    activation_transaction.ErrorHandlingMode = TransactionErrorHandlingMode.Ignore;

                transaction.Tasks.Add(activation_transaction);
            }

            return transaction;
        }
    }
}
