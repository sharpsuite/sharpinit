using System;
using SharpInit.Units;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using SharpInit.Ipc;
using NLog;

using Mono.Unix;
using Mono.Unix.Native;
using SharpInit.Platform;

namespace SharpInit
{
    class Program
    {
        static TimeSpan DaemonStartOffset = TimeSpan.Zero;
        static System.Diagnostics.Stopwatch DaemonStart = System.Diagnostics.Stopwatch.StartNew();

        public static TimeSpan ElapsedSinceStartup() => DaemonStart.Elapsed + DaemonStartOffset;

        static Logger Log = LogManager.GetCurrentClassLogger();
        static IpcListener IpcListener { get; set; }
        public static ServiceManager ServiceManager { get; private set; }
        public static async Task Main(string[] args)
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

                    try 
                    {
                        platform_init.Initialize();
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"Platform initialization failed.");
                        Log.Error(ex);
                        Log.Error($"Continuing with startup anyway.");
                    }

                    Log.Info("Platform initialization complete");
                    Log.Info("Starting service manager...");
                    ServiceManager = new ServiceManager();

                    new Thread(ServiceManager.Runner.Run).Start();

                    var journal_target = new JournalTarget(ServiceManager.Journal) { Layout = "[${uppercase:${level}}:${level:format=Ordinal}] ${message}" };
                    var config = LogManager.Configuration;
                    config.AddTarget("journal", journal_target);
                    config.AddRuleForAllLevels("journal");

                    if (PlatformUtilities.CurrentlyOn("linux"))
                    {
                        if (System.IO.Directory.Exists("/run/udev/tags"))
                        {
                            try
                            {
                                Platform.Unix.UdevEnumerator.ServiceManager = ServiceManager;
                                Platform.Unix.UdevEnumerator.InitializeHandlers();
                                Platform.Unix.UdevEnumerator.ScanDevicesByTag("systemd");
                                Platform.Unix.UdevEnumerator.ScanDevicesByTag("sharpinit");
                            }
                            catch (Exception ex)
                            {
                                Log.Warn(ex, $"Failed to retrieve devices from udevd.");
                            }
                        }
                        else
                        {
                            Log.Info($"udevd is not running.");
                        }
                    }

                    if (PlatformUtilities.CurrentlyOn("linux") && SharpInit.Platform.Unix.UnixPlatformInitialization.IsSystemManager)
                    {
                        var journal_fd = SharpInit.Platform.Unix.UnixPlatformInitialization.JournalOutputFd;

                        if (journal_fd > 1)
                        {
                            Log.Info($"Rerouting log output to fd {journal_fd}");
                            config.RemoveTarget("stdout");
                            
                            ServiceManager.Journal.JournalDataReceived += (s, e) => 
                            {
                                if (e.Entry.LogLevel < 2)
                                    return;
                                
                                var kern_loglevel = 7 - e.Entry.LogLevel;

                                var text = e.Data;
                                var rendered = $"sharpinit[1]: [ {e.Source} ] {text}";

                                SharpInit.Platform.Unix.UnixUtilities.WriteToFd(journal_fd, rendered);
                            };
                        }
                    }

                    LogManager.Configuration = config;

                    NLog.NestedDiagnosticsLogicalContext.Push("main");

                    ServiceManager.Registry.ScanDefaultDirectories();
                    
                    Log.Info($"Loaded {ServiceManager.Registry.Units.Count} units");

                    ServiceManager.UnitStateChanged += StateChangeHandler;

                    Log.Info("Starting socket manager...");
                    ServiceManager.SocketManager.StartSelectLoop();

                    Log.Info("Registering IPC context...");

                    var context = new ServerIpcContext(ServiceManager);
                    IpcFunctionRegistry.AddFunction(DynamicIpcFunction.FromContext(context));

                    Log.Info("Starting IPC listener...");

                    IpcListener = new IpcListener();
                    IpcListener.StartListening();

                    Log.Info($"Listening on {IpcListener.SocketEndPoint}");

                    ServiceManager.InitializeCGroups();

                    if (!args.Any(a => a == "--no-activate-default")) 
                    {
                        ActivateUnitIfExists("default.target");
                    }

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

            foreach (var unit in ServiceManager.Registry.Units)
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
            if (ServiceManager.Registry.GetUnit(name) != null)
            {
                Log.Info($"Activating {name}...");
                var tx = ServiceManager.Planner.CreateActivationTransaction(name, "Main thread");
                Log.Debug($"This is the transaction:");
                Log.Debug(tx.GenerateTree());

                var exec = ServiceManager.Runner.Register(tx).Enqueue();
                exec.Wait();
                var result = exec.Result;

                if (result.Type == Tasks.ResultType.Success)
                    Log.Info($"Successfully activated {name}.");
                else
                    Log.Info($"Error while activating {name}: {result.Type}, {result.Message}");
            }
        }

        private static void DeactivateUnitIfExists(string name)
        {
            if (ServiceManager.Registry.GetUnit(name) != null)
            {
                Log.Info($"Deactivating {name}...");
                var tx = ServiceManager.Planner.CreateDeactivationTransaction(name, "Main thread");
                Log.Debug($"This is the transaction:");
                Log.Debug(tx.GenerateTree());

                var exec = ServiceManager.Runner.Register(tx).Enqueue();
                exec.Wait();
                var result = exec.Result;

                if (result.Type == Tasks.ResultType.Success)
                    Log.Info($"Successfully deactivated {name}.");
                else
                    Log.Info($"Error while deactivating {name}: {result.Type}, {result.Message}");
            }
        }

        private static void StateChangeHandler(object sender, UnitStateChangedEventArgs e)
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
