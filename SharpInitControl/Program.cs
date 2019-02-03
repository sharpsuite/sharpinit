using SharpInit.Ipc;
using System;
using System.Collections.Generic;
using System.Linq;
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
            {"reload", ReloadUnits }
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
    }
}
