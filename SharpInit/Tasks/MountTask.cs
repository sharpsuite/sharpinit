using System;
using System.Collections.Generic;
using System.IO;

using SharpInit.Units;
using SharpInit.Platform;
using SharpInit.Platform.Unix;

using Mono.Unix;

namespace SharpInit.Tasks
{
    public class MountTask : Task
    {
        public override string Type => "mount";

        public MountUnit Unit { get; set; }
        public MountUnitDescriptor Descriptor => Unit.Descriptor;

        public MountTask(MountUnit unit)
        {
            Unit = unit;
        }

        public override TaskResult Execute(TaskContext context)
        {
            try
            {
                if (!PlatformUtilities.CurrentlyOn("unix"))
                    return new TaskResult(this, ResultType.Failure, "Mounting is only supported on Unix systems.");
                
                if (Unit.UnitName != StringEscaper.EscapePath(Descriptor.Where) + ".mount")
                    return new TaskResult(this, ResultType.Failure, $"Mount unit name does not match Where= parameter: \"{Unit.UnitName}\" has \"Where={Descriptor.Where}\", expected unit name to be \"{StringEscaper.EscapePath(Descriptor.Where)}.mount\"");
                
                var mount_type = Descriptor.Type ?? "auto";
                var args = new List<string>() { "--types", mount_type };

                if (!string.IsNullOrWhiteSpace(Descriptor.Options))
                {
                    args.Add("--options");
                    args.Add(Descriptor.Options);
                }
                
                if (Descriptor.SloppyOptions)
                    args.Add("-s");
                
                if (Descriptor.ReadWriteOnly)
                    args.Add("-w");
                
                args.AddRange(new [] { "--source", Descriptor.What, "--target", Descriptor.Where });

                // Create target directory if it doesn't exist.
                var dir = Descriptor.Where;
                if (!Directory.Exists(dir))
                {
                    var discarded_segments = new List<string>();
                    
                    while (!Directory.Exists(dir) && Path.GetDirectoryName(dir) != dir) 
                    {
                        discarded_segments.Add(Path.GetFileName(dir));
                        dir = Path.GetDirectoryName(dir);
                    }
                    
                    discarded_segments.Reverse();

                    foreach (var segment in discarded_segments)
                    {
                        var concatted = dir + "/" + segment;
                        Directory.CreateDirectory(concatted);
                        new UnixDirectoryInfo(concatted).FileAccessPermissions = (FileAccessPermissions)Descriptor.DirectoryMode;
                        dir = concatted;
                    }
                }

                var psi = new ProcessStartInfo()
                {
                    Path = "/bin/mount",
                    Arguments = args.ToArray(),
                    Unit = Unit,
                    Timeout = Descriptor.TimeoutSec,
                    StandardOutputTarget = "null",
                    StandardErrorTarget = "null",
                    StandardInputTarget = "null"
                };

                var task = new RunRegisteredProcessTask(psi, Unit, wait_for_exit: true, exit_timeout: (int)Descriptor.TimeoutSec.TotalMilliseconds);
                var result = ExecuteYielding(task, context);

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