using System;
using SharpInit.Units;
using System.Threading;
using System.Threading.Tasks;
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
        static IpcListener IpcListener { get; set; }
        public static async Task Main()
        {
            var done = new ManualResetEventSlim(false);
            using (var shutdownCts = new CancellationTokenSource())
            {
                try
                {
                    AttachCtrlcSigtermShutdown(shutdownCts, done);
                    Log.Info("SharpInit starting");

                    PlatformUtilities.RegisterImplementations();
                    var platform_init = PlatformUtilities.GetImplementation<IPlatformInitialization>();
                    platform_init.Initialize();

                    Log.Info("Platform initialization complete");

                    var journal_target = new JournalTarget(UnitRegistry.ServiceManager.Journal) { Layout = "${date} [${uppercase:${level}}] ${message}" };
                    var config = LogManager.Configuration;
                    config.AddTarget("journal", journal_target);
                    config.AddRuleForAllLevels("journal");
                    LogManager.Configuration = config;

                    NLog.NestedDiagnosticsLogicalContext.Push("main");

                    UnitRegistry.ScanDefaultDirectories();
                    
                    Log.Info($"Loaded {UnitRegistry.Units.Count} units");

                    UnitRegistry.UnitStateChange += StateChangeHandler;

                    Log.Info("Starting socket manager...");
                    UnitRegistry.SocketManager.StartSelectLoop();

                    Log.Info("Registering IPC context...");

                    var context = new ServerIpcContext();
                    IpcFunctionRegistry.AddFunction(DynamicIpcFunction.FromContext(context));

                    Log.Info("Starting IPC listener...");

                    IpcListener = new IpcListener();
                    IpcListener.StartListening();

                    Log.Info($"Listening on {IpcListener.SocketEndPoint}");

                    ActivateUnitIfExists("sockets.target");
                    ActivateUnitIfExists("default.target");

                    Log.Info("Starting late platform initialization...");
                    platform_init.LateInitialize();
                    Log.Info("Late platform initialization complete");
                    
                    try 
                    {
                        await Task.Delay(Timeout.Infinite, shutdownCts.Token);
                    }
                    catch (Exception ex) { Log.Error(ex); }
                    
                    Log.Info($"Shutting down...");
                    Shutdown();
                }
                finally
                {
                    done.Set();
                }
            }
        }

        public static void Shutdown()
        {
            DeactivateUnitIfExists("sockets.target");
            DeactivateUnitIfExists("default.target");

            foreach (var unit in UnitRegistry.Units)
            {
                if (unit.Value.CurrentState.HasFlag(SharpInit.Units.UnitState.Activating) || 
                    unit.Value.CurrentState.HasFlag(SharpInit.Units.UnitState.Active))
                {
                    DeactivateUnitIfExists(unit.Value.UnitName);
                }
            }

            IpcListener.Stop();
            Log.Info("Goodbye!");
            Environment.FailFast(null);
        }

        private static void ActivateUnitIfExists(string name)
        {
            if (UnitRegistry.GetUnit(name) != null)
            {
                Log.Info($"Activating {name}...");
                var result = UnitRegistry.CreateActivationTransaction(name, "Main thread").Execute();

                if (result.Type == Tasks.ResultType.Success)
                    Log.Info($"Successfully activated {name}.");
                else
                    Log.Info($"Error while activating {name}: {result.Type}, {result.Message}");
            }
        }

        private static void DeactivateUnitIfExists(string name)
        {
            if (UnitRegistry.GetUnit(name) != null)
            {
                Log.Info($"Deactivating {name}...");
                var result = UnitRegistry.CreateDeactivationTransaction(name, "Main thread").Execute();

                if (result.Type == Tasks.ResultType.Success)
                    Log.Info($"Successfully deactivated {name}.");
                else
                    Log.Info($"Error while deactivating {name}: {result.Type}, {result.Message}");
            }
        }

        private static void StateChangeHandler(object sender, UnitStateChangeEventArgs e)
        {
            if (e.Reason != null)
                Log.Info($"Unit {e.Unit.UnitName} is transitioning from {e.Unit.CurrentState} to {e.NextState}: \"{e.Reason}\"");
            else
                Log.Info($"Unit {e.Unit.UnitName} is transitioning from {e.Unit.CurrentState} to {e.NextState} (no reason specified)");
        }

        // Based on Microsoft.AspNetCore.Hosting.WebHostExtensions.AttachCtrlcSigtermShutdown
        private static void AttachCtrlcSigtermShutdown(CancellationTokenSource cts, ManualResetEventSlim resetEvent)
        {
            void PropagateShutdown()
            {
                try
                {
                    cts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }
                resetEvent.Wait();
            };

            AppDomain.CurrentDomain.ProcessExit += (sender, eventArgs) => PropagateShutdown();
            Console.CancelKeyPress += (sender, eventArgs) =>
            {
                PropagateShutdown();
                // Don't terminate the process immediately, wait for the Main thread to exit gracefully.
                eventArgs.Cancel = true;
            };
        }
    }
}
