using System;
using SharpInit.Units;

namespace SharpInit
{
    class Program
    {
        static void Main(string[] args)
        {
            var unit = UnitParser.Parse<UnitFile>("./test.unit");
            Console.WriteLine(unit);
        }
    }
}
