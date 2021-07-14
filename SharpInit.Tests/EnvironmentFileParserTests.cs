using Microsoft.VisualStudio.TestTools.UnitTesting;
using SharpInit.Platform;
using SharpInit.Units;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SharpInit.Tests
{
    [TestClass]
    public class EnvironmentFileParserTests
    {
        [ClassInitialize]
        public static void ClassInitialize(TestContext value)
        {
            PlatformUtilities.RegisterImplementations();
            PlatformUtilities.GetImplementation<IPlatformInitialization>().Initialize();
            UnitRegistry.InitializeTypes();
        }

        public static string[] TestFileContents = new [] {
"# This is a test of the environment file parser.",
"INTERFACES=\"\"",
"",
"; Some other comments.",
"; Hi!",
"; Incoming is a multi-line environmental variable.",
"",
"DRIVERS=+usb_storage +soundcore +videodev +rfkill +rapl \\",
"        +ac97 +intel_hda +drm +gpu_sched +radeon +joydev \\",
"        +nf_nat +squashfs +vfat +loop +kvm_amd +sunrpc",
"",
"; Now one with the quotes.",
"DEVICES=\"00:03.1 00:03.2 \\",
"00:03.3 00:03.4\"",
""};

        [TestMethod]
        public void EnvironmentFileParser_Test()
        {
            var parsed = UnitParser.ParseEnvironmentFile(string.Join("\n", TestFileContents)).GroupBy(p => p.Key).ToDictionary(p => p.Key, p => p.Select(q => q.Value));

            Assert.IsTrue(parsed["DEVICES"].Contains("00:03.1 00:03.2 00:03.3 00:03.4"));
            Assert.IsTrue(parsed["DRIVERS"].Contains("+usb_storage +soundcore +videodev +rfkill +rapl +ac97 +intel_hda +drm +gpu_sched +radeon +joydev +nf_nat +squashfs +vfat +loop +kvm_amd +sunrpc"));
            Assert.IsTrue(parsed["INTERFACES"].Contains(""));
        }
    }
}