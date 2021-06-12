using System;
using SharpInit.Units;
using System.Threading;
using SharpInit.Ipc;
using NLog;

using Mono.Unix;
using Mono.Unix.Native;
using SharpInit.Platform;

namespace SharpInit
{
    class Program
    {
        static Logger Log = LogManager.GetCurrentClassLogger();

        static void Main(string[] args)
        {
            Log.Info("SharpInit starting");

            PlatformUtilities.RegisterImplementations();
            var platform_init = PlatformUtilities.GetImplementation<IPlatformInitialization>();
            platform_init.Initialize();

            Log.Info("Platform initialization complete");

            UnitRegistry.ScanDefaultDirectories();
            
            Log.Info($"Loaded {UnitRegistry.Units.Count} units");

            UnitRegistry.UnitStateChange += StateChangeHandler;

            Log.Info("Starting socket manager...");
            UnitRegistry.SocketManager.StartSelectLoop();

            Log.Info("Registering IPC context...");

            var context = new ServerIpcContext();
            IpcFunctionRegistry.AddFunction(DynamicIpcFunction.FromContext(context));

            Log.Info("Starting IPC listener...");

            var ipc_listener = new IpcListener();
            ipc_listener.StartListening();

            Log.Info($"Listening on {ipc_listener.SocketEndPoint}");

            ActivateUnitIfExists("sockets.target");
            ActivateUnitIfExists("default.target");

            Console.CancelKeyPress += (s, e) =>
            {
                ipc_listener.StopListening();
            };
            
            Thread.Sleep(-1);
        }

        private static void ActivateUnitIfExists(string name)
        {
            if (UnitRegistry.GetUnit(name) != null)
            {
                Log.Info($"Activating {name}...");
                var result = UnitRegistry.CreateActivationTransaction(name).Execute();

                if (result.Type == Tasks.ResultType.Success)
                    Log.Info($"Successfully activated {name}.");
                else
                    Log.Info($"Error while activating {name}: {result.Type}, {result.Message}");
            }
        }

        private static void StateChangeHandler(Unit source, Units.UnitState next_state)
        {
            Log.Info($"Unit {source.UnitName} is transitioning from {source.CurrentState} to {next_state}");
        }
    }
}
