using Microsoft.VisualStudio.TestTools.UnitTesting;
using SharpInit.Platform;
using SharpInit.Units;
using System;
using System.IO;
using System.Text;

namespace UnitTests.Units
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
        }

        [ClassCleanup]
        static public void ClassCleanup()
        {
            File.Delete(TestUnitPath);
            Directory.Delete(DirectoryName, true);
            Environment.SetEnvironmentVariable("SHARPINIT_UNIT_PATH", null);
        }

        [TestMethod]
        public void AddUnit_UnitIsAdded_True()
        {
            // Arrange
            Unit unit = new ServiceUnit(TestUnitPath);

            // Act
            UnitRegistry.AddUnit(unit);

            // Assert
            Assert.IsTrue(UnitRegistry.Units.Count == 1);
        }

        [TestMethod]
        public void AddUnit_UnitIsNull_True()
        {
            // Arrange
            Unit unit = null;

            // Act
            UnitRegistry.AddUnit(unit);

            // Assert
            Assert.IsTrue(UnitRegistry.Units.Count == 0);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void AddUnit_UnitHasAlreadyBeenAdded_ThrowsException()
        {
            // Arrange
            Unit unit = new ServiceUnit(TestUnitPath);

            // Act
            UnitRegistry.AddUnit(unit);
            UnitRegistry.AddUnit(unit);

            // Assert
            // Exception Thrown
        }
        
        [TestMethod]
        public void AddUnitByPath_UnitFound_True()
        {
            // Arrange

            // Act
            UnitRegistry.AddUnitByPath(TestUnitPath);

            // Assert
            Assert.IsTrue(UnitRegistry.Units.Count == 1);
        }

        [TestMethod]
        public void AddUnitByPath_UnitNotFound_True()
        {
            // Arrange

            // Act
            UnitRegistry.AddUnitByPath($"{DirectoryName}/nonexistent-unit.service");

            // Assert
            Assert.IsTrue(UnitRegistry.Units.Count == 0);
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
            Assert.IsTrue(UnitRegistry.Units.Count > 0);
        }
    }
}
