using SharpInit.Ipc;
using System;
using System.Collections.Generic;
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
            {"status", GetUnitStatus }
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
            Connection.Tunnel.Socket.Close();
            Connection.Tunnel.Close();
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
                    status_color = ConsoleColor.Gray;
                    break;
                case UnitState.Failed:
                    status_color = ConsoleColor.Red;
                    break;
            }

            Console.ForegroundColor = status_color;
            Console.Write("•");
            Console.ForegroundColor = ConsoleColor.Gray;

            Console.WriteLine($" {status.Name}{(!string.IsNullOrWhiteSpace(status.Description) ? " - " + status.Description : "")}");
            Console.WriteLine($"Loaded from: {status.Path} at {status.LoadTime.ToLocalTime()}");
            Console.Write("Status: ");

            Console.ForegroundColor = status_color;
            Console.Write(status.State.ToString().ToLower());
            Console.ForegroundColor = ConsoleColor.Gray;

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
                Console.WriteLine($"{list.Count} units loaded: [{string.Join(",", list)}]");
        }
    }
}
