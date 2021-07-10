using Microsoft.VisualStudio.TestTools.UnitTesting;
using SharpInit.Platform;
using SharpInit.Units;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SharpInit.Tests
{
    [TestClass]
    public class SlicePathTests
    {
        [ClassInitialize]
        public static void ClassInitialize(TestContext value)
        {
            PlatformUtilities.RegisterImplementations();
            PlatformUtilities.GetImplementation<IPlatformInitialization>().Initialize();
            UnitRegistry.InitializeTypes();
        }

        public Dictionary<string, string> SlicesWithParents = new Dictionary<string, string>()
        {
            {"-.slice", "-.slice"},
            {"machine.slice", "-.slice"},
            {"user-1000.slice", "user.slice"},
        };

        public Dictionary<string, string> SlicesWithPaths = new Dictionary<string, string>()
        {
            {"-.slice", "/"},
            {"machine.slice", "/machine.slice"},
            {"user-1000.slice", "/user.slice/user-1000.slice"},
            {"user-1000-session.slice", "/user.slice/user-1000.slice/user-1000-session.slice"},
        };

        [TestMethod]
        public void SliceParent_Test()
        {
            foreach (var pair in SlicesWithParents)
                Assert.AreEqual(pair.Value, SliceUnit.GetParentSlice(pair.Key));
        }

        [TestMethod]
        public void SliceToCGroupPath_Test()
        {
            foreach (var pair in SlicesWithPaths)
                Assert.AreEqual(pair.Value, SliceUnit.GetCGroupPath(pair.Key));
        }
    }
}