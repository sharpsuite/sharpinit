using System;
using System.IO;

using Mono.Unix.Native;

using SharpInit;
using SharpInit.Units;
using SharpInit.Platform;
using SharpInit.Platform.Unix;

using NLog;

namespace SharpInit.Tasks
{
    public class ManipulateTtyTask : Task
    {
        static Logger Log = LogManager.GetCurrentClassLogger();

        public override string Type => "manipulate-tty";
        public ExecUnitDescriptor Descriptor { get; set; }

        public ManipulateTtyTask(ExecUnitDescriptor descriptor)
        {
            Descriptor = descriptor;
        }

        public override TaskResult Execute(TaskContext context)
        {
            if (!PlatformUtilities.CurrentlyOn("unix"))
                return new TaskResult(this, ResultType.SoftFailure, "TTYs are only supported on Unix");
            
            try
            {
                if (Descriptor == null)
                    return new TaskResult(this, ResultType.Failure, "No descriptor specified");
                
                if (!File.Exists(Descriptor.TtyPath))
                    return new TaskResult(this, ResultType.Failure, $"Could not find TTY {Descriptor.TtyPath}");
                
                if (Descriptor.TtyVHangup)
                {
                    Log.Trace($"VhangupTty says: {TtyUtilities.VhangupTty(Descriptor.TtyPath)}");
                }
                
                if (Descriptor.TtyReset)
                {
                    Log.Trace($"ResetTty says: {TtyUtilities.ResetTty(Descriptor.TtyPath)}");
                }

                if (Descriptor.TtyVtDisallocate)
                {
                    Log.Trace($"DisallocateTty says: {TtyUtilities.DisallocateTty(Descriptor.TtyPath)}");
                }

                return new TaskResult(this, ResultType.Success);
            }
            catch (Exception ex)
            {
                return new TaskResult(this, ResultType.Failure, ex.Message);
            }
        }
    }
}