using System;
using System.Collections.Generic;
using System.Linq;
using SharpInit.Units;

namespace SharpInit.Tasks
{
    public class CheckUnitConditionsTask : Task
    {
        public override string Type => "check-unit-conditions";

        public Unit Unit { get; set; }

        public CheckUnitConditionsTask(Unit unit)
        {
            Unit = unit;
        }

        private bool CheckConditionGroup(List<KeyValuePair<string, string>> conditions)
        {
            var triggering = conditions.Where(p => p.Value.StartsWith('|'));
            var non_triggering = conditions.Where(p => !p.Value.StartsWith('|'));

            return (!non_triggering.Any() ? true : non_triggering.All(p => UnitConditions.CheckCondition(p.Key, p.Value))) && 
                   (!triggering.Any() ? true : triggering.Any(p => UnitConditions.CheckCondition(p.Key, p.Value)));
        }

        public override TaskResult Execute(TaskContext context)
        {
            try
            {
                var assertions = Unit?.Descriptor?.Assertions?.SelectMany(p => p.Value.Select(v => new KeyValuePair<string, string>(p.Key, v)));
                var conditions = Unit?.Descriptor?.Conditions?.SelectMany(p => p.Value.Select(v => new KeyValuePair<string, string>(p.Key, v)));

                if (assertions != null)
                {
                    var result = CheckConditionGroup(assertions.ToList());

                    if (!result)
                    {
                        var failed_assertions = assertions.Where(a => !UnitConditions.CheckCondition(a.Key, a.Value)).Select(a => $"{a.Key}={a.Value}");
                        Runner.ExecuteBlocking(new SetUnitStateTask(Unit, UnitState.Inactive, reason: $"Unit startup assertions failed: [{string.Join(", ", failed_assertions)}]"), Execution.Context);
                        return new TaskResult(this, ResultType.StopExecution, $"Assertions failed: [{string.Join(", ", failed_assertions)}]");
                    }
                }

                if (conditions != null)
                {
                    var result = CheckConditionGroup(conditions.ToList());

                    if (!result)
                    {
                        var failed_conditions = conditions.Where(c => !UnitConditions.CheckCondition(c.Key, c.Value)).Select(c => $"{c.Key}={c.Value}");
                        Runner.ExecuteBlocking(new SetUnitStateTask(Unit, UnitState.Inactive, reason: $"Unit startup conditions failed: [{string.Join(", ", failed_conditions)}]"), Execution.Context);
                        return new TaskResult(this, ResultType.StopExecution, $"Conditions failed: [{string.Join(", ", failed_conditions)}]");
                    }
                }
                
                return new TaskResult(this, ResultType.Success);
            }
            catch (Exception ex)
            {
                return new TaskResult(this, ResultType.Failure | ResultType.StopExecution, ex.Message);
            }
        }
    }
}