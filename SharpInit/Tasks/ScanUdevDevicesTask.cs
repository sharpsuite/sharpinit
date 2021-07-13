using System;

using SharpInit.Platform.Unix;

namespace SharpInit.Tasks
{
    public class ScanUdevDevicesTask : Task
    {
        public override string Type => "scan-udev";

        public override TaskResult Execute(TaskContext context)
        {
            try
            {
                if (!SharpInit.Platform.PlatformUtilities.CurrentlyOn("linux"))
                    return new TaskResult(this, ResultType.SoftFailure, "udev is not supported.");
                
                if (!System.IO.Directory.Exists("/run/udev"))
                    return new TaskResult(this, ResultType.Failure, "/run/udev does not exist.");
                
                UdevEnumerator.ScanDevicesByTag("systemd");
                UdevEnumerator.ScanDevicesByTag("sharpinit");

                return new TaskResult(this, ResultType.Success);
            }
            catch (Exception ex)
            {
                return new TaskResult(this, ResultType.SoftFailure, ex.Message);
            }
        }
    }
}