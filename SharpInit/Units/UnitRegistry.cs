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
            var req_deps = RequirementDependencies.GetDependencies(unit.UnitName);
            var order_deps = OrderingDependencies.GetDependencies(unit.UnitName);

            var unit_list = new List<Unit>();

            var transaction_dictionary = new Dictionary<string, Transaction>();
            var transaction_ignore_failure = new Dictionary<string, bool>();
            var transaction = new Transaction();

            transaction.Add(unit.GetActivationTransaction());
            transaction_dictionary[unit.UnitName] = transaction;
            
            var req_graph = RequirementDependencies.TraverseDependencyGraph(unit.UnitName, t => t.RequirementType != RequirementDependencyType.Conflicts, false);
            
            foreach(var dependency in req_graph)
            {
                var parent = dependency.LeftUnit;
                var child = dependency.RightUnit;

                if (transaction_dictionary.ContainsKey(child))
                    continue;

                var target_unit = GetUnit(child);

                if(!unit_list.Contains(target_unit))
                    unit_list.Add(target_unit);

                //var sub_transaction = target_unit.GetActivationTransaction();

                //if (dependency.RequirementType == RequirementDependencyType.Wants)
                //    sub_transaction.ErrorHandlingMode = TransactionErrorHandlingMode.Ignore;
                //else
                //    sub_transaction.ErrorHandlingMode = TransactionErrorHandlingMode.Fail;
                
                //if (!transaction_dictionary.ContainsKey(parent))
                //    parent = unit.UnitName;

                //transaction_dictionary[parent].Add(sub_transaction);
                //transaction_dictionary[child] = sub_transaction;
            }

            string current_unit = unit.UnitName;
            var list = new List<RequirementDependency>();

            unit_list.Add(unit);
            transaction_ignore_failure[current_unit] = false;

            while (true)
            {
                var dependencies_to_resolve = req_graph.Where(dep => dep.LeftUnit == current_unit).ToList();

                foreach (var dependency in dependencies_to_resolve)
                {
                    if(dependency.RequirementType == RequirementDependencyType.Wants)
                    {
                        transaction_ignore_failure[dependency.RightUnit] = transaction_ignore_failure.ContainsKey(dependency.RightUnit) ? transaction_ignore_failure[dependency.RightUnit] : true;
                    }
                    else
                    {
                        transaction_ignore_failure[dependency.RightUnit] = false;
                        list.Add(dependency);
                    }
                }

                if (!list.Any())
                    break;

                current_unit = list.First().RightUnit;
                list.RemoveAt(0);
            }

            transaction = new Transaction();

            foreach(var sub_unit in unit_list)
            {
                var activation_transaction = sub_unit.GetActivationTransaction();

                if (transaction_ignore_failure.ContainsKey(sub_unit.UnitName) && !transaction_ignore_failure[sub_unit.UnitName])
                    activation_transaction.ErrorHandlingMode = TransactionErrorHandlingMode.Fail;
                else
                    activation_transaction.ErrorHandlingMode = TransactionErrorHandlingMode.Ignore;

                transaction.Tasks.Add(activation_transaction);
            }

            return transaction;
        }
    }
}
