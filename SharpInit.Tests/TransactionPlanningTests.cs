using Microsoft.VisualStudio.TestTools.UnitTesting;
using SharpInit.Platform;
using SharpInit.Units;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Linq;

namespace SharpInit.Tests
{
    [TestClass]
    public class TransactionPlanningTests
    {
        public static ServiceManager ServiceManager { get; private set; }
        public static UnitRegistry Registry => ServiceManager.Registry;
        public static TransactionPlanner Planner => ServiceManager.Planner;

        [ClassInitialize]
        public static void ClassInitialize(TestContext value)
        {
            PlatformUtilities.RegisterImplementations();
            PlatformUtilities.GetImplementation<IPlatformInitialization>().Initialize();
            UnitRegistry.InitializeTypes();
            ServiceManager = new ServiceManager();
        }

        [TestMethod]
        public void UnitOrdering_Test()
        {
            var random = new Random(0);

            // getty.target before default.target
            // getty.service after getty.target
            // getty.service after default.target
            
            // getty.target
            // default.target
            // getty.service
            
            int unit_count = 50;
            int target_every = 10;

            var base_target_name = "test-target-main.target";
            var base_target = new GeneratedUnitFile(base_target_name);

            var unit_names = new List<string>() { base_target_name };
            var unit_files = new List<GeneratedUnitFile>() { base_target };
            var targeted = new Dictionary<string, List<string>>();
            var targets = new List<string>();

            var untargeted_units = new List<string>();

            for (int i = 0; i < unit_count; i++)
            {
                if ((i > 0 && i % target_every == 0) || 
                    (i == unit_count - 1 && untargeted_units.Any()))
                {
                    targets.Add($"test-target-{i}.target");
                    unit_names.Add($"test-target-{i}.target");
                    targeted[$"test-target-{i}.target"] = untargeted_units.ToList();
                    untargeted_units.Clear();
                }
                else
                {
                    unit_names.Add($"test-unit-{i}.service");
                    untargeted_units.Add($"test-unit-{i}.service");
                }
            }

            var last_target = "";

            foreach (var target_with_units in targeted)
            {
                var target_index = targets.IndexOf(target_with_units.Key);
                var target_file = new GeneratedUnitFile(target_with_units.Key).WithProperty("Unit/Before", base_target_name);

                if (last_target != "")
                    target_file = target_file.WithProperty("Unit/After", last_target);

                base_target.WithProperty("Unit/Wants", target_file.UnitName);
                
                var generated_unit_files = new List<GeneratedUnitFile>();
                
                foreach (var unit in target_with_units.Value)
                {
                    var unit_file = new GeneratedUnitFile(unit)
                        .WithProperty("Unit/Before", target_file.UnitName)
                        .WithProperty("Service/Type", "oneshot");

                    if (last_target != "")
                        unit_file = unit_file.WithProperty("Unit/After", last_target);
                    
                    target_file.WithProperty("Unit/Wants", unit);
                    //target_file.WithProperty("Unit/After", unit);
                    generated_unit_files.Add(unit_file);
                }
                
                // target-5
                // target-10
                // unit-11,12,13,14
                // target-15
                
                for (int i = 0; i < generated_unit_files.Count; i++)
                {
                    var unit = generated_unit_files[i];

                    if (random.NextDouble() > 0.5)
                    {
                        var statement = random.NextDouble() > 0.5 ? "After" : "Before";
                        var other_unit_index = statement == "After" ? random.Next(0, i) : random.Next(i + 1, generated_unit_files.Count);

                        if (other_unit_index == i || other_unit_index >= generated_unit_files.Count)
                            continue;
                        
                        unit.WithProperty($"Unit/{statement}", generated_unit_files[other_unit_index].UnitName);
                    }

                    if (random.NextDouble() > 0.5 && target_index != 0)
                    {
                        unit.WithProperty($"Unit/After", targets[random.Next(0, target_index)]);
                    }
                }
                
                generated_unit_files.Add(target_file);
                unit_files.AddRange(generated_unit_files);
                last_target = target_file.UnitName;
            }

            Assert.IsTrue(unit_files.All(f => Registry.IndexUnitFile(f)));
            Registry.Reload();

            var tx = Planner.CreateActivationTransaction(base_target_name);

            var unit_activations_by_name = tx.AffectedUnits.Select(u => u.UnitName).ToList();
            var unit_activation_indices = unit_names.ToDictionary(unit => unit, unit => unit_activations_by_name.IndexOf(unit));
            var target_activation_indices = targeted.Select(target => unit_activation_indices[target.Key]).ToList();

            //Assert.IsTrue(unit_activations_by_name.Count == unit_count + 1);
            Assert.IsTrue(unit_names.All(u => unit_activation_indices[u] <= unit_activation_indices[base_target_name]));
            Assert.IsTrue(targeted.All(target => target.Value.All(unit => unit_activation_indices[unit] < unit_activation_indices[target.Key])));
            Assert.IsTrue(target_activation_indices.SequenceEqual(target_activation_indices.OrderBy(i => i)));
        }
    }
}