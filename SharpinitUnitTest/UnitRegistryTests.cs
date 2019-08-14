using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
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

        static string path = ".\\test.service";
        static string contents =
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
            UnitRegistry.ScanDefaultDirectories();

            File.WriteAllText(path,contents); 
        }

        [TestCleanup]
        public void TestCleanup()
        {
            UnitRegistry.Units.Clear();
        }

        [ClassCleanup]
        static public void ClassCleanup()
        {
            File.Delete(path);
            Environment.SetEnvironmentVariable("SHARPINIT_UNIT_PATH", null);
        }

        [TestMethod]
        public void AddUnit_UnitIsAdded_True()
        {
            // Arrange
            Unit unit = new ServiceUnit(path);

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
            Unit unit = new ServiceUnit(path);

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
            UnitRegistry.AddUnitByPath(path);

            // Assert
            Assert.IsTrue(UnitRegistry.Units.Count == 1);
        }

        [TestMethod]
        public void AddUnitByPath_UnitNotFound_True()
        {
            // Arrange

            // Act
            UnitRegistry.AddUnitByPath("");

            // Assert
            Assert.IsTrue(UnitRegistry.Units.Count == 0);
        }

        [TestMethod]
        public void ScanDefaultDirectories_UnitsFound_True()
        {
            // Arrange
            Environment.SetEnvironmentVariable("SHARPINIT_UNIT_PATH", ".");
            var check = Environment.GetEnvironmentVariable("SHARPINIT_UNIT_PATH");
            // Act
            var result = UnitRegistry.ScanDefaultDirectories();

            // Assert
            Assert.IsTrue(UnitRegistry.Units.Count > 0);
        }
        /*
        [TestMethod]
        public void ScanDirectory_StateUnderTest_ExpectedBehavior()
        {
            // Arrange
            
            string path = null;
            bool recursive = false;

            // Act
            var result = unitRegistry.ScanDirectory(
                path,
                recursive);

            // Assert
            Assert.Fail();
        }

        [TestMethod]
        public void GetUnit_StateUnderTest_ExpectedBehavior()
        {
            // Arrange
            
            string name = null;

            // Act
            var result = unitRegistry.GetUnit(
                name);

            // Assert
            Assert.Fail();
        }

        [TestMethod]
        public void CreateUnit_StateUnderTest_ExpectedBehavior()
        {
            // Arrange
            
            string path = null;

            // Act
            var result = unitRegistry.CreateUnit(
                path);

            // Assert
            Assert.Fail();
        }

        [TestMethod]
        public void InitializeTypes_StateUnderTest_ExpectedBehavior()
        {
            // Arrange
            

            // Act
            unitRegistry.InitializeTypes();

            // Assert
            Assert.Fail();
        }

        [TestMethod]
        public void CreateActivationTransaction_StateUnderTest_ExpectedBehavior()
        {
            // Arrange
            
            string name = null;

            // Act
            var result = unitRegistry.CreateActivationTransaction(
                name);

            // Assert
            Assert.Fail();
        }

        [TestMethod]
        public void CreateActivationTransaction_StateUnderTest_ExpectedBehavior1()
        {
            // Arrange
            
            Unit unit = null;

            // Act
            var result = unitRegistry.CreateActivationTransaction(
                unit);

            // Assert
            Assert.Fail();
        }

        [TestMethod]
        public void CreateDeactivationTransaction_StateUnderTest_ExpectedBehavior()
        {
            // Arrange
            
            string unit = null;

            // Act
            var result = unitRegistry.CreateDeactivationTransaction(
                unit);

            // Assert
            Assert.Fail();
        }

        [TestMethod]
        public void CreateDeactivationTransaction_StateUnderTest_ExpectedBehavior1()
        {
            // Arrange
            
            Unit unit = null;

            // Act
            var result = unitRegistry.CreateDeactivationTransaction(
                unit);

            // Assert
            Assert.Fail();
        }
        */
    }
   
}
