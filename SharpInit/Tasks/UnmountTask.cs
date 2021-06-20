using System;
using System.Collections.Generic;
using System.IO;

using SharpInit.Units;
using SharpInit.Platform;
using SharpInit.Platform.Unix;

using Mono.Unix;

namespace SharpInit.Tasks
{
    public class UnmountTask : Task
    {
        public override string Type => "unmount";

        public MountUnit Unit { get; set; }
        public MountUnitDescriptor Descriptor => Unit.Descriptor;

        public UnmountTask(MountUnit unit)
        {
            Unit = unit;
        }

        public override TaskResult Execute(TaskContext context)
        {
            try
            {
                if (!PlatformUtilities.CurrentlyOn("unix"))
                    return new TaskResult(this, ResultType.Failure, "Mounting is only supported on Unix systems.");
                
                var args = new List<string>();

                if (Descriptor.LazyUnmount)
                    args.Add("-l");
                
                if (Descriptor.ForceUnmount)
                    args.Add("-f");

                args.Add(Descriptor.Where);                

                var psi = new ProcessStartInfo()
                {
                    Path = "/bin/umount",
                    Arguments = args.ToArray(),
                    Unit = Unit,
                    User = new UnixUserIdentifier(0),
                    Timeout = Descriptor.TimeoutSec,
                    StandardOutputTarget = "journal",
                    StandardErrorTarget = "journal"
                };

                var task = new RunRegisteredProcessTask(psi, Unit, wait_for_exit: true, exit_timeout: (int)Descriptor.TimeoutSec.TotalMilliseconds);
                var result = task.Execute(context);

                if (result.Message != "exit code 0")
                    return new TaskResult(this, ResultType.Failure, result.Message);
                
                return result;
            }
            catch (Exception ex)
            {
                return new TaskResult(this, ResultType.Failure, ex.Message);
            }
        }
    }
}