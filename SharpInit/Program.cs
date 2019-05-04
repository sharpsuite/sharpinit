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
            PlatformUtilities.GetImplementation<IPlatformInitialization>().Initialize();

            UnitRegistry.InitializeTypes();
            UnitRegistry.ScanDefaultDirectories();
            
            Log.Info($"Loaded {UnitRegistry.Units.Count} units");

            UnitRegistry.UnitStateChange += StateChangeHandler;

            Log.Info("Registering IPC context...");

            var context = new ServerIpcContext();
            IpcFunctionRegistry.AddFunction(DynamicIpcFunction.FromContext(context));

            Log.Info("Starting IPC listener...");

            var ipc_listener = new IpcListener();
            ipc_listener.StartListening();

            Log.Info($"Listening on {ipc_listener.SocketEndPoint}");

            Console.CancelKeyPress += (s, e) =>
            {
                ipc_listener.StopListening();
            };
            
            Thread.Sleep(-1);
        }

        private static void StateChangeHandler(Unit source, Units.UnitState next_state)
        {
            Log.Info($"Unit {source.UnitName} is transitioning from {source.CurrentState} to {next_state}");
        }
    }
}
