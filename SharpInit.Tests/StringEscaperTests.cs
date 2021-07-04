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
    public class StringEscaperTests
    {
        [ClassInitialize]
        public static void ClassInitialize(TestContext value)
        {
            PlatformUtilities.RegisterImplementations();
            PlatformUtilities.GetImplementation<IPlatformInitialization>().Initialize();
            UnitRegistry.InitializeTypes();
        }

        public Dictionary<string, string> Paths = new Dictionary<string, string>()
        {
            {"/", "-"},
            {"/foo//bar/baz", "foo-bar-baz"},
            {"/home/kate", "home-kate"},
            {"/home/kate/.config/(cache)", "home-kate-.config-\\x28cache\\x29"}
        };

        [TestMethod]
        public void PathEscape_Test()
        {
            foreach (var pair in Paths)
                Assert.AreEqual(pair.Value, StringEscaper.EscapePath(pair.Key));
        }

        [TestMethod]
        public void PathUnescape_Test()
        {
            foreach (var pair in Paths)
                Assert.AreEqual(Path.GetFullPath(pair.Key), StringEscaper.UnescapePath(pair.Value));
        }
    }
}