using Microsoft.VisualStudio.TestTools.UnitTesting;
using SharpInit.Platform;
using SharpInit.Tasks;
using SharpInit.Units;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SharpInit.Tests
{
    [TestClass]
    public class AsyncTests
    {
        static ServiceManager ServiceManager { get; set; }

        [ClassInitialize]
        public static void ClassInitialize(TestContext value)
        {
            PlatformUtilities.RegisterImplementations();
            PlatformUtilities.GetImplementation<IPlatformInitialization>().Initialize();
            UnitRegistry.InitializeTypes();
            ServiceManager = new ServiceManager();
            new System.Threading.Thread(ServiceManager.Runner.Run).Start();
        }

        [TestMethod]
        public async System.Threading.Tasks.Task WaitForStateChange_Test()
        {
            var test_target_file = new GeneratedUnitFile("test-target.target")
                .WithProperty("Unit/Description", "Test target")
                .WithProperty("Unit/DefaultDependencies", "no");

            ServiceManager.Registry.IndexUnitFile(test_target_file);

            var target_unit = ServiceManager.Registry.GetUnit(test_target_file.UnitName);

            ServiceManager.Runner.Register(ServiceManager.Planner.CreateActivationTransaction(target_unit)).Enqueue().Wait();

            var state_change_waiter = target_unit.FailUnless(UnitState.Inactive, TimeSpan.FromMilliseconds(1000));
            ServiceManager.Runner.Register(state_change_waiter);
            var state_change_exec_result = state_change_waiter.ExecuteAsync(null);

            await System.Threading.Tasks.Task.Delay(300);
            ServiceManager.Runner.Register(ServiceManager.Planner.CreateDeactivationTransaction(target_unit)).Enqueue().Wait();

            Assert.IsTrue(state_change_exec_result.Result.Type == Tasks.ResultType.Success);

            var state_change_waiter_other = target_unit.FailUnless(UnitState.Active, TimeSpan.FromMilliseconds(1000));
            ServiceManager.Runner.Register(state_change_waiter_other);
            var state_change_exec_result_other = state_change_waiter_other.ExecuteAsync(null);

            Assert.IsTrue(state_change_exec_result_other.Result.Type.HasFlag(Tasks.ResultType.Timeout));
        }
    }
}