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
    public class UnitRegistryTests
    {
        static string DirectoryName = "";
        static string TestUnitFilename = "test.service";
        static string TestUnitPath => Path.Combine(DirectoryName, TestUnitFilename);

        static string TestUnitContents =
                "[Unit]\n" +
                "Description = Notepad\n" +
                "\n" +
                "[Service]\n" +
                "ExecStart = notepad.exe";

        [ClassInitialize]
        public static void ClassInitialize(TestContext value)
        {
            PlatformUtilities.RegisterImplementations();
            PlatformUtilities.GetImplementation<IPlatformInitialization>().Initialize();
            UnitRegistry.InitializeTypes();

            Random rnd = new Random();
            var random_buffer = new byte[8];

            rnd.NextBytes(random_buffer);
            DirectoryName = BitConverter.ToString(random_buffer).Replace("-", "").ToLower();
            Directory.CreateDirectory(DirectoryName);

            File.WriteAllText(TestUnitPath, TestUnitContents); 
        }

        [TestCleanup]
        public void TestCleanup()
        {
            UnitRegistry.Units.Clear();
            UnitRegistry.UnitFiles.Clear();
        }

        [ClassCleanup]
        static public void ClassCleanup()
        {
            File.Delete(TestUnitPath);
            Directory.Delete(DirectoryName, true);
            Environment.SetEnvironmentVariable("SHARPINIT_UNIT_PATH", null);
        }
        
        [TestMethod]
        public void AddUnitByPath_UnitFound_True()
        {
            // Arrange
            UnitRegistry.UnitFiles.Clear();
            UnitRegistry.Units.Clear();

            // Act
            UnitRegistry.IndexUnitFile(TestUnitPath);

            // Assert
            Assert.IsNotNull(UnitRegistry.GetUnit(TestUnitFilename));
        }

        [TestMethod]
        public void AddUnitByPath_UnitNotFound_True()
        {
            // Arrange
            UnitRegistry.UnitFiles.Clear();
            UnitRegistry.Units.Clear();

            // Act
            UnitRegistry.IndexUnitFile($"{DirectoryName}/nonexistent-unit.service");

            // Assert
            Assert.IsNull(UnitRegistry.GetUnit(TestUnitFilename));
        }

        [TestMethod]
        public void ScanDefaultDirectories_UnitsFound_True()
        {
            // Arrange
            Environment.SetEnvironmentVariable("SHARPINIT_UNIT_PATH", DirectoryName);
            UnitRegistry.DefaultScanDirectories.Clear();

            // Act
            var result = UnitRegistry.ScanDefaultDirectories();

            // Assert
            Assert.IsTrue(UnitRegistry.UnitFiles.ContainsKey(TestUnitFilename));
        }

        [TestMethod]
        public void GetUnitName_Checks()
        {
            var dictionary = new Dictionary<string, string>()
            {
                {"/home/user/.config/sharpinit/units/test@1001.service", "test.service" },
                {"/etc/sharpinit/units/test@.service", "test.service" },
                {"C:\\Users\\User\\.config\\sharpinit\\units\\notepad.service", "notepad.service" },
                {"relative/path/to/sshd@22.service", "sshd.service" },
                {"backslash\\relative\\path\\test.target", "test.target" }

            };

            foreach (var pair in dictionary)
                Assert.AreEqual(UnitRegistry.GetUnitName(pair.Key), pair.Value);
        }
    }
}