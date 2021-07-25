﻿using SharpInit.Tasks;
using System;
using System.Collections.Generic;
using System.Text;

namespace SharpInit.Units
{
    /// <summary>
    /// A special transaction, generated by UnitRegistry's transaction planner. Includes metadata that describes
    /// why each unit is changing state.
    /// </summary>
    public class UnitStateChangeTransaction : Transaction
    {
        public UnitStateChangeType ChangeType { get; set; }
        public Unit SourceUnit { get; set; }
        public List<Unit> AffectedUnits { get; set; }
        public Dictionary<Unit, List<string>> Reasoning { get; set; }
        public Task Precheck { get; set; }
        public bool PrintStatus { get; set; }

        public UnitStateChangeTransaction(Unit source_unit, Dictionary<Unit, List<string>> reasoning)
        {
            AffectedUnits = new List<Unit>();
            SourceUnit = source_unit;
            Reasoning = reasoning;
            PrintStatus = true;
        }

        public UnitStateChangeTransaction(Unit source_unit) :
            this(source_unit, new Dictionary<Unit, List<string>>() { { source_unit, new List<string>() { $"{source_unit.UnitName} is changing state because it's being asked to" } } })
        {

        }

        public UnitStateChangeTransaction(Unit source_unit, UnitStateChangeType type, string name = null) :
            this(source_unit)
        {
            Name = name ?? $"{type} transaction for {source_unit.UnitName}";
            ChangeType = type;
        }

        static Dictionary<(string, UnitStateChangeType), (string, string)> GenericStatusMessages = new Dictionary<(string, UnitStateChangeType), (string, string)>()
        {
            {("pre", UnitStateChangeType.Activation), (null, "Starting {0}...")},
            {("pre", UnitStateChangeType.Deactivation), (null, "Stopping {0}...")},

            {("success", UnitStateChangeType.Activation), ("  OK  ", "Started {0}.")},
            {("success", UnitStateChangeType.Deactivation), ("  OK  ", "Stopped {0}.")},

            {("timeout", UnitStateChangeType.Activation), (" TIME ", "Timed out starting {0}.")},
            {("timeout", UnitStateChangeType.Deactivation), (" TIME ", "Timed out stopping {0}.")},

            {("error", UnitStateChangeType.Activation), ("FAILED", "Failed to start {0}.")},
            {("error", UnitStateChangeType.Deactivation), ("FAILED", "Failed to stop {0}.")},
        };

        private (string, string) GetStatusMessage(string type, UnitStateChangeType change_type, Unit unit = null)
        {
            unit = unit ?? SourceUnit;

            if (unit.StatusMessages?.ContainsKey((type, change_type)) == true)
                return unit.StatusMessages[(type, change_type)];

            if (GenericStatusMessages.ContainsKey((type, change_type)))
                return GenericStatusMessages[(type, change_type)];

            return ("  ??  ", "{0} is changing state.");
        }

        public async override System.Threading.Tasks.Task<TaskResult> ExecuteAsync(TaskContext context = null)
        {
            using (ActingTaskMarker.Mark(SourceUnit, this))
            using (NLog.NestedDiagnosticsLogicalContext.Push(SourceUnit.UnitName)) 
            {
                bool should_run = true;

                if (Precheck != null)
                {
                    var precheck_result = Runner.ExecuteBlocking(Precheck, context);
                    if (precheck_result.Type.HasFlag(ResultType.StopExecution))
                        should_run = false;
                }

                if (!should_run)
                {
                    return new TaskResult(this, ResultType.Success | ResultType.Skipped);
                }

                PrintStatusMessage(GetStatusMessage("pre", ChangeType), ephemeral: true);

                foreach (var task in Tasks)
                {
                    if (task is UnitStateChangeTransaction tx && tx.SourceUnit == SourceUnit)
                        tx.PrintStatus = false;
                }

                var result = await base.ExecuteAsync(context);

                if (result.Type.HasFlag(ResultType.Skipped))
                {
                    return result;
                }
                else if (result.Type.HasFlag(ResultType.Success))
                {
                    PrintStatusMessage(GetStatusMessage("success", ChangeType));
                }
                else if (result.Type.HasFlag(ResultType.Timeout))
                {
                    PrintStatusMessage(GetStatusMessage("timeout", ChangeType));
                }
                else if (result.Type.HasFlag(ResultType.Failure))
                {
                    PrintStatusMessage(GetStatusMessage("failure", ChangeType));
                }

                return result;
            }
        }

        private static bool LastMessageEphemeral = false;

        private void PrintStatusMessage(ValueTuple<string, string> tuple, bool ephemeral = false) =>
            PrintStatusMessage(tuple.Item1, tuple.Item2, ephemeral);
        
        private static object StatusLock = new object();
        private void PrintStatusMessage(string tag, string text, bool ephemeral)
        {
            if (!PrintStatus)
                return;

            var indent_str = new string(' ', 8);

            tag = tag?.Trim('[', ']');
            if (tag != null)
                tag = $"[{tag}]";

            tag = tag ?? indent_str;

            text = string.Format(text, (SourceUnit?.Descriptor?.Description ?? SourceUnit?.UnitName) ?? "unknown unit");

            var message = $"{tag} {StringEscaper.Truncate(text, 50)}";

            lock (StatusLock)
            {
                if (LastMessageEphemeral && Platform.PlatformUtilities.CurrentlyOn("unix"))
                {
                    message = "\u001b[F\u001b[2K\r" + message;
                }

                LastMessageEphemeral = ephemeral;

                if (Platform.PlatformUtilities.CurrentlyOn("linux") && Platform.Unix.UnixPlatformInitialization.IsSystemManager)
                {
                    var console = Mono.Unix.Native.Syscall.open("/dev/console", Mono.Unix.Native.OpenFlags.O_WRONLY | Mono.Unix.Native.OpenFlags.O_NOCTTY | Mono.Unix.Native.OpenFlags.O_CLOEXEC);

                    if (console < 0)
                        return;
                    
                    SharpInit.Platform.Unix.UnixUtilities.WriteToFd(console, message + "\n");
                    Mono.Unix.Native.Syscall.close(console);
                }   
                else
                {
                    Console.WriteLine(message);
                }
            }
        }
    }

    public class ActingTaskMarker : IDisposable
    {
        private Task _previous_task { get; set; }
        public Unit Unit { get; set; }
        public Task Task { get; set; }

        public static ActingTaskMarker Mark(Unit unit, Task task)
        {
            var m = new ActingTaskMarker();

            m.Unit = unit;
            m.Task = task;
            
            if (unit != null)
            {
                m._previous_task = m.Unit.ActingTask;
                m.Unit.ActingTask = task;
            }

            return m;
        }

        void IDisposable.Dispose()
        {
            if (Unit != null)
            {
                Unit.ActingTask = _previous_task;
            }
        }
    }

    public enum UnitStateChangeType
    {
        Unknown,
        Activation,
        Deactivation
    }
}