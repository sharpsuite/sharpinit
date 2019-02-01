using System;
using SharpInit.Units;
using System.Threading;

namespace SharpInit
{
    class Program
    {
        static void Main(string[] args)
        {
            UnitRegistry.InitializeTypes();
            UnitRegistry.ScanDirectory("./units", true);

            var default_service_manager = new ServiceManager();

            var unit = UnitRegistry.GetUnit("notepad.service");
            unit.UnitStateChange += StateChangeHandler;
            unit.ServiceManager = default_service_manager;
            unit.Activate();

            Thread.Sleep(5000);

            unit.Deactivate();
            Console.WriteLine(unit);
            Console.ReadLine();
        }

        private static void StateChangeHandler(Unit source, UnitState next_state)
        {
            Console.WriteLine($"Unit {source.UnitName} is transitioning from {source.CurrentState} to {next_state}");
        }
    }
}
