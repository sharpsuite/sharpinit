using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using SharpInit.Units;

namespace SharpInit.Tasks
{
    /// <summary>
    /// Pulls a service unit's stored file descriptors (see sd_notify) into the transaction context, so that they're
    /// passed to the process being started in RunRegisteredProcessTask.
    /// </summary>
    public class ForgetStoredFileDescriptorsTask : Task
    {
        public override string Type => "forget-stored-file-descriptors";
        public ServiceUnit Unit { get; set; }

        public ForgetStoredFileDescriptorsTask(ServiceUnit unit)
        {
            Unit = unit;
        }

        public override TaskResult Execute(TaskContext context)
        {
            if (Unit != null && Unit.StoredFileDescriptors != null)
            {
                Unit.StoredFileDescriptors.Clear();
            }
            
            return new TaskResult(this, ResultType.Success);
        }
    }
}