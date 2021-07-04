using SharpInit.Ipc;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Runtime.InteropServices;

namespace SharpInitControl
{
    public delegate void HandleCommand(string verb, string[] args);

    class Program
    {
        static Dictionary<string, HandleCommand> Commands = new Dictionary<string, HandleCommand>()
        {
            {"start", StartUnits },
            {"stop", StopUnits },
            {"restart", RestartUnits },
            {"reload", ReloadUnits },
            {"list", ListUnits },
            {"list-files", ListUnitFiles },
            {"daemon-reload", RescanUnits },
            {"status", GetUnitStatus },
            {"describe-deps", DescribeDependencies },
            {"load", LoadUnit },
            {"journal", GetJournal},
            {"enable", InstallUnits},
            {"disable", UninstallUnits}
        };

        static IpcConnection Connection { get; set; }
        static ClientIpcContext Context { get; set; }

        static void Main(string[] args)
        {
            Connection = new IpcConnection();
            Connection.Connect();
            Context = new ClientIpcContext(Connection, "sharpinitctl");

            var verb = args[0].ToLower();

            if(!Commands.ContainsKey(verb))
            {
                Console.WriteLine($"Unknown verb \"{verb}\".");
                Console.WriteLine($"Known verbs are: {string.Join(",", Commands.Keys)}");
                Environment.Exit(1);
            }
            
            Commands[verb](verb, args.Skip(1).ToArray());
            
            Environment.Exit(0);
        }

        static void GetJournal(string verb, string[] args)
        {
            var count = int.Parse(args.FirstOrDefault(a => int.TryParse(a, out int _)) ?? "50");
            var journal = args.FirstOrDefault(a => a != count.ToString());

            var lines = Context.GetJournal(journal, count);

            var use_less = RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? 
                ((Environment.GetEnvironmentVariable("USE_PAGER") ?? "true") == "true") : false;

            if (use_less)
            {
                var temp = Path.GetTempFileName();
                File.WriteAllLines(temp, lines);

                var psi = new System.Diagnostics.ProcessStartInfo();
                psi.FileName = "/usr/bin/env";

                var pager_opts = Environment.GetEnvironmentVariable("PAGER_OPTS") ?? "-cS";
                var pager = Environment.GetEnvironmentVariable("PAGER") ?? "less";

                psi.Arguments = $"{pager} {pager_opts} {temp}";
                System.Diagnostics.Process.Start(psi).WaitForExit();
                File.Delete(temp);
            }
            else
            {
                foreach (var line in lines)
                    Console.WriteLine(line);
            }
        }

        static void DescribeDependencies(string verb, string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                var unit = args[i];
                var activation_plan = Context.GetActivationPlan(unit);
                var deactivation_plan = Context.GetDeactivationPlan(unit);

                Console.WriteLine($"Activation plan for {unit}:");
                foreach (var pair in activation_plan)
                {
                    var reasons_text = pair.Value.Count == 1 ? "" : $" ({pair.Value.Count} reasons)";
                    var prefix = pair.Value.Count == 1 ? "  " : "  + ";
                    Console.WriteLine($"{prefix}{pair.Key}{reasons_text}:");
                    foreach (var reason in pair.Value)
                    {
                        Console.WriteLine($"      {reason}");
                    }
                }
                //activation_plan.Select(t => ).ToList().ForEach(Console.WriteLine);

                Console.WriteLine();

                Console.WriteLine($"Dectivation plan for {unit}:");
                foreach (var pair in deactivation_plan)
                {
                    var reasons_text = pair.Value.Count == 1 ? "" : $" ({pair.Value.Count} reasons)";
                    var prefix = pair.Value.Count == 1 ? "  " : "  + ";
                    Console.WriteLine($"{prefix}{pair.Key}{reasons_text}:");
                    foreach (var reason in pair.Value)
                    {
                        Console.WriteLine($"      {reason}");
                    }
                }
            }
        }

        static void RescanUnits(string verb, string[] args)
        {
            Console.Write("Rescanning unit directories...");

            var loaded_units = Context.RescanUnits();

            if (loaded_units >= 0)
                Console.WriteLine("loaded {0} units in total", loaded_units);
            else
                Console.WriteLine("error");
        }

        static void GetUnitStatus(string verb, string[] args)
        {
            var unit = args.First();
            var status = Context.GetUnitInfo(unit);

            if (status == null)
            {
                Console.WriteLine($"Unknown unit {unit}");
                return;
            }

            var current_state_color = UnitStateToConsoleColor(status.CurrentState);
            var previous_state_color = UnitStateToConsoleColor(status.PreviousState);
            var foreground_color = Console.ForegroundColor;

            Console.OutputEncoding = Encoding.UTF8;
            
            PrintWithColor("•", current_state_color);
            Console.WriteLine($" {status.Name}{(!string.IsNullOrWhiteSpace(status.Description) ? " - " + status.Description : "")}");
            Console.WriteLine($"Loaded from: {status.Path} at {status.LoadTime.ToLocalTime()}");
            Console.Write("Status: ");

            PrintWithColor(status.CurrentState.ToString().ToLower(), current_state_color);
            Console.Write($", last activated {PrintPastOccurrence(status.ActivationTime)}");
            Console.WriteLine();

            Console.Write($"Previous state: ");
            PrintWithColor(status.PreviousState.ToString().ToLower(), previous_state_color);
            Console.WriteLine($", changed {PrintPastOccurrence(status.LastStateChangeTime)}");

            if (!string.IsNullOrWhiteSpace(status.StateChangeReason))
            {
                Console.WriteLine($"State change reason: {status.StateChangeReason}");
            }

            Console.WriteLine();

            foreach (var line in status.LogLines)
            {
                Console.WriteLine(line);
            }

            Console.WriteLine();
        }
        static void InstallUnits(string verb, string[] args)
        {
            foreach(var unit in args)
            {
                Console.Write($"Installing {unit}...");

                var result = Context.InstallUnit(unit);

                if (result)
                    Console.WriteLine("success");
                else
                    Console.WriteLine("error");
            }
        }
        
        static void UninstallUnits(string verb, string[] args)
        {
            foreach(var unit in args)
            {
                Console.Write($"Uninstalling {unit}...");

                var result = Context.UninstallUnit(unit);

                if (result)
                    Console.WriteLine("success");
                else
                    Console.WriteLine("error");
            }
        }
        static void StartUnits(string verb, string[] args)
        {
            foreach(var unit in args)
            {
                Console.Write($"Starting {unit}...");

                var result = Context.ActivateUnit(unit);

                if (result)
                    Console.WriteLine("done");
                else
                    Console.WriteLine("error");
            }
        }

        static void StopUnits(string verb, string[] args)
        {
            foreach (var unit in args)
            {
                Console.Write($"Stopping {unit}...");

                var result = Context.DeactivateUnit(unit);

                if (result)
                    Console.WriteLine("done");
                else
                    Console.WriteLine("error");
            }
        }

        static void RestartUnits(string verb, string[] args)
        {
            foreach (var unit in args)
            {
                Console.Write($"Stopping {unit}...");

                var result = Context.DeactivateUnit(unit);

                if (result)
                    Console.WriteLine("done");
                else
                    Console.WriteLine("error");

                Console.Write($"Starting {unit}...");

                result = Context.ActivateUnit(unit);

                if (result)
                    Console.WriteLine("done");
                else
                    Console.WriteLine("error");
            }
        }

        static void ReloadUnits(string verb, string[] args)
        {
            foreach (var unit in args)
            {
                Console.Write($"Reloading {unit}...");

                var result = Context.ReloadUnit(unit);

                if (result)
                    Console.WriteLine("done");
                else
                    Console.WriteLine("error");
            }
        }

        static void ListUnits(string verb, string[] args)
        {
            var list = Context.ListUnits();

            if (list == null)
                Console.WriteLine("Couldn't retrieve the list of loaded units.");
            else
                Console.WriteLine($"{list.Count} units loaded: [{string.Join(", ", list)}]");
        }

        static void ListUnitFiles(string verb, string[] args)
        {
            var list = Context.ListUnitFiles();

            if (list == null)
                Console.WriteLine("Couldn't retrieve the list of loaded unit files.");
            else
                Console.WriteLine($"{list.Count} unit files loaded: [{string.Join(", ", list)}]");
        }

        static void LoadUnit(string verb, string[] args)
        {
            var path = Path.GetFullPath(args[0]);

            if (File.Exists(path))
            {
                var success = Context.LoadUnitFromFile(path);
                if (success)
                {
                    Console.WriteLine($"Successfully indexed unit at \"{path}\".");
                }
                else
                {
                    Console.WriteLine($"Failed to index unit at \"{path}\".");
                }
            }
        }
        
        static string PrintPastOccurrence(DateTime time)
        {
            if (time == DateTime.MinValue)
                return "never";
            
            return $"{TimeSpanToPrettyString(DateTime.UtcNow - time)} ago; {time.ToLocalTime()}";
        }

        public static string Pluralize(int number, string noun)
        {
            if (Math.Abs(number) == 1)
                return $"{number} {noun}";

            if (noun.EndsWith("y") && !noun.EndsWith("ay"))
                noun = noun.Substring(0, noun.Length - 1) + "ie";
            
            return $"{number} {noun}s";
        }

        public static string Pluralize(double number, string noun) => Pluralize((int)number, noun);
        public static string TimeSpanToPrettyString(TimeSpan span)
        {
            Dictionary<string, int> lengths = new Dictionary<string, int>()
            {
                {Pluralize(span.TotalDays / 7, "week"), (int)(span.TotalDays / 7) },
                {Pluralize(span.TotalDays % 7, "day"), (int)(span.TotalDays % 7) },
                {Pluralize(span.TotalHours % 24, "hour"), (int)(span.TotalHours % 24) },
                {Pluralize(span.TotalMinutes % 60, "minute"), (int)(span.TotalMinutes % 60) },
                {Pluralize(span.TotalSeconds % 60, "second"), (int)(span.TotalSeconds % 60) },
            };

            var final = lengths.Where(p => p.Value > 0).Select(p => p.Key).Take(2);

            return string.Join(" ", final);
        }

        static void PrintWithColor(string text, ConsoleColor color)
        {
            var foreground = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.Write(text);
            Console.ForegroundColor = foreground;
        }

        static ConsoleColor UnitStateToConsoleColor(UnitState state)
        {
            var default_color = Console.ForegroundColor;
            var status_color = ConsoleColor.Gray;

            switch (state)
            {
                case UnitState.Activating:
                case UnitState.Active:
                case UnitState.Reloading:
                    status_color = ConsoleColor.Green;
                    break;
                case UnitState.Deactivating:
                case UnitState.Inactive:
                    status_color = default_color;
                    break;
                case UnitState.Failed:
                    status_color = ConsoleColor.Red;
                    break;
            }

            return status_color;
        }
    }
}
