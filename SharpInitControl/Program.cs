﻿using SharpInit.Ipc;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Runtime.InteropServices;
using Mono.Unix;

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
            {"disable", UninstallUnits},
            {"join-manager-to-current-cgroup", JoinCGroup},
            {"service-manager-pid", PrintServiceManagerPid},
            {"show", ShowUnit},
            {"list-seats", ListSeats}
        };

        static IpcConnection Connection { get; set; }
        static ClientIpcContext Context { get; set; }

        static void Main(string[] args)
        {
            Connection = new IpcConnection();
            Connection.InitializeSocket();
            
            if (args[0] == "--user")
            {
                var runtime_path = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
                var socket_path = $"{runtime_path}/sharpinit.sock";

                if (!Directory.Exists(runtime_path))
                {
                    Console.WriteLine($"XDG_RUNTIME_DIR invalid, cannot guess location of user service manager.");
                    return;
                }

                Connection.SocketEndPoint = new UnixEndPoint(socket_path);

                args = args.Skip(1).ToArray();
            }
            
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

        static void ListSeats(string verb, string[] args)
        {
            var seats = Context.ListSeats();

            foreach (var pair in seats)
            {
                Console.WriteLine($"Seat {pair.Key}: ");

                foreach (var device in pair.Value)
                {
                    Console.WriteLine($" - {device}");
                }
                
                Console.WriteLine();
            }
        }

        static void PrintServiceManagerPid(string verb, string[] args) => Console.WriteLine(Context.GetServiceManagerProcessId());

        static void JoinCGroup(string verb, string[] args)
        {
            string target_cgroup = "";

            if (!args.Any())
            {
                target_cgroup = File.ReadAllText("/proc/self/cgroup");
            }
            else
                target_cgroup = args.First();
            
            target_cgroup = target_cgroup.Trim();
            target_cgroup = target_cgroup.TrimStart(':', '0');
            
            var manager_pid = Context.GetServiceManagerProcessId();
            var manager_uid = Mono.Unix.UnixFileSystemInfo.GetFileSystemEntry($"/proc/{manager_pid}").OwnerUserId;
            var manager_gid = Mono.Unix.UnixFileSystemInfo.GetFileSystemEntry($"/proc/{manager_pid}").OwnerGroupId;

            Console.Write($"Moving to cgroup {target_cgroup}...");

            try 
            { 
                var psi = new System.Diagnostics.ProcessStartInfo("/usr/bin/chown", $"-R {manager_uid}:{manager_gid} /sys/fs/cgroup{target_cgroup}");
                System.Diagnostics.Process.Start(psi).WaitForExit();

                File.WriteAllText($"/sys/fs/cgroup{target_cgroup}/cgroup.procs", manager_pid.ToString()); 
                Console.WriteLine(Context.MoveToCGroup(target_cgroup) ? "success" : "error");
            } 
            catch (Exception ex) 
            { 
                Console.WriteLine("error"); 
                Console.WriteLine(ex);
            }
        }

        static void GetJournal(string verb, string[] args)
        {
            var count = int.Parse(args.FirstOrDefault(a => int.TryParse(a, out int _)) ?? "10000");
            var journal = args.FirstOrDefault(a => a != count.ToString()) ?? "main";

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

        static void ShowUnit(string verb, string[] args)
        {
            var unit = args.First();
            var props = Context.GetUnitProperties(unit);

            if (props == null || !props.Any())
                return;
            
            foreach (var pair in props)
            {
                foreach (var val in pair.Value)
                    Console.WriteLine($"{pair.Key}={val}");
            }
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

            if (status.MainProcessId > 0)
                Console.WriteLine($"Main process: {status.MainProcessId}");

            if (status.ProcessTree.Any())
            {
                Console.Write($"CGroup: ");

                var offset = Console.CursorLeft;

                foreach (var line in status.ProcessTree)
                {
                    Console.CursorLeft = offset;
                    string l = line;

                    if (l.Length > (Console.WindowWidth - Console.CursorLeft))
                        l = l.Substring(0, (Console.WindowWidth - (Console.CursorLeft + 3))) + "...";

                    Console.WriteLine(l);
                }
                
                Console.WriteLine();
            }

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
