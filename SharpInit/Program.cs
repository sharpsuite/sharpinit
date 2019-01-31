using System;
using SharpInit.Units;

namespace SharpInit
{
    class Program
    {
        static void Main(string[] args)
        {
            UnitRegistry.InitializeTypes();
            UnitRegistry.ScanDirectory("./units", true);

            var unit = UnitRegistry.GetUnit("sshd.service");
            Console.WriteLine(unit);
        }
    }
}
