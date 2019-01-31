using System;

namespace SharpInit
{
    class Program
    {
        static void Main(string[] args)
        {
            UnitFile.Parse<UnitFile>("./test.unit");
        }
    }
}
