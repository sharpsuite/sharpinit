using SharpInit.Ipc;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

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
            {"load", LoadUnit }
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
            
            Connection.Disconnect();
            Connection.Tunnel.Close();
            Environment.Exit(0);
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

            Console.OutputEncoding = Encoding.UTF8;
            var default_color = Console.ForegroundColor;
            var status_color = ConsoleColor.Gray;

            switch (status.State)
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

            Console.ForegroundColor = status_color;
            Console.Write("•");
            Console.ForegroundColor = default_color;

            Console.WriteLine($" {status.Name}{(!string.IsNullOrWhiteSpace(status.Description) ? " - " + status.Description : "")}");
            Console.WriteLine($"Loaded from: {status.Path} at {status.LoadTime.ToLocalTime()}");
            Console.Write("Status: ");

            Console.ForegroundColor = status_color;
            Console.Write(status.State.ToString().ToLower());
            Console.ForegroundColor = default_color;

            Console.Write($" (last activated {(status.ActivationTime == DateTime.MinValue ? "never" : status.ActivationTime.ToLocalTime().ToString())})");
            Console.WriteLine();
            Console.WriteLine();
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
    }
}
