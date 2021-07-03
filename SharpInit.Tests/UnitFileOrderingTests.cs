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
    public class UnitFileOrderingTests
    {
        [ClassInitialize]
        public static void ClassInitialize(TestContext value)
        {
            PlatformUtilities.RegisterImplementations();
            PlatformUtilities.GetImplementation<IPlatformInitialization>().Initialize();
            UnitRegistry.InitializeTypes();
        }

        [TestMethod]
        public void UnitFileOrdering_Test()
        {
            // Ordered from lowest priority to highest priority. This is the expected ordering when sorted by CompareUnitFiles.
            var test_paths = new List<string>()
            {
                "/run/sharpinit/generator.late/test.service",
                "/usr/lib/sharpinit/system/test.service", 
                "/usr/local/lib/sharpinit/system/test.service", 
                "/run/sharpinit/generator/test.service", 
                "/run/sharpinit/system/test.service", 
                "/etc/sharpinit/system/test.service", 
                "/run/sharpinit/generator.early/test.service", 
                "/run/sharpinit/transient/test.service", 
                "/run/sharpinit/system.control/test.service", 
                "/etc/sharpinit/system.control/test.service", 
            };

            var reversed_paths = test_paths.Reverse<string>();
            var as_unit_files = reversed_paths.Select(path => new OnDiskUnitFile(path)).ToList();
            as_unit_files.Sort(UnitRegistry.CompareUnitFiles); 

            for (int i = 0; i < test_paths.Count; i++)
                Assert.AreEqual(test_paths[i], as_unit_files[i].Path);
        }
    }
}