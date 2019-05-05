using SharpInit.Platform;
using SharpInit.Units;
using System;
using System.Collections.Generic;
using System.Text;

namespace SharpInit.Tasks
{
    public class RunRegisteredProcessTask : Task
    {
        public override string Type => "run-registered-process";
        public ProcessStartInfo ProcessStartInfo { get; set; }
        public Unit Unit { get; set; }

        public RunRegisteredProcessTask(ProcessStartInfo psi, Unit unit)
        {
            ProcessStartInfo = psi;
            Unit = unit;
        }

        public override TaskResult Execute()
        {
            if (ProcessStartInfo == null || Unit == null)
                return new TaskResult(this, ResultType.Failure, "No ProcessStartInfo or Unit supplied.");

            try
            {
                Unit.ServiceManager.StartProcess(Unit, ProcessStartInfo);
                return new TaskResult(this, ResultType.Success);
            }
            catch (Exception ex)
            {
                return new TaskResult(this, ResultType.Failure, ex.Message);
            }
        }
    }
}
