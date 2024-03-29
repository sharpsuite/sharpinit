﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using SharpInit.Ipc;
using NLog;

using Mono.Unix;
using SharpInit.Platform;
using SharpInit.Tasks;

using SharpInit.Platform.Unix;
using SharpInit.Units;

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
        public static Platform.Unix.LoginManagement.LoginManager LoginManager { get; internal set; }
        
        public static bool IsUserManager { get; set; }
        
        public static async System.Threading.Tasks.Task Main(string[] args)
        {
            var done = new ManualResetEventSlim(false);
            using (var shutdownCts = new CancellationTokenSource())
            {
                try
                {
                    //AttachCtrlcSigtermShutdown(shutdownCts, done);
                    Log.Info("SharpInit starting");

                    IsUserManager = args.Any(a => a == "--user");
                    
                    Log.Info($"SharpInit is the {(IsUserManager ? "user" : "system")} manager");

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
                        UdevEnumerator.ServiceManager = ServiceManager;
                        new Thread((ThreadStart) delegate { UdevEnumerator.WaitForUdevAndInitialize(); }).Start();
                    }

                    if (PlatformUtilities.CurrentlyOn("linux") && UnixPlatformInitialization.IsSystemManager)
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

                    ServiceManager.RunOnQueue(() => ServiceManager.Registry.ScanDefaultDirectories(), "scan-default-dirs");
                    
                    Log.Info($"Loaded {ServiceManager.Registry.Units.Count} units");

                    ServiceManager.UnitStateChanged += StateChangeHandler;

                    Log.Info("Starting socket manager...");
                    ServiceManager.SocketManager.StartSelectLoop();

                    Log.Info("Registering IPC context...");

                    var context = new ServerIpcContext(ServiceManager);
                    IpcFunctionRegistry.AddFunction(DynamicIpcFunction.FromContext(context));

                    Log.Info("Starting IPC listener...");

                    IpcListener = new IpcListener();
                    IpcListener.InitializeSocket();

                    if (Program.IsUserManager)
                    {
                        var runtime_path = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
                        var socket_path = $"{runtime_path}/sharpinit.sock";

                        if (!Directory.Exists(runtime_path))
                        {
                            socket_path = $"/tmp/sharpinit.{Process.GetCurrentProcess().Id}.sock";
                            Log.Warn($"XDG_RUNTIME_DIR invalid, will listen at {runtime_path}");
                        }

                        IpcListener.SocketEndPoint = new UnixEndPoint(socket_path);
                    }
                    
                    IpcListener.StartListening();

                    Log.Info($"Listening on {IpcListener.SocketEndPoint}");
                    
                    ServiceManager.InitializeCGroups();

                    if (!args.Any(a => a == "--no-activate-default")) 
                    {
                        ActivateUnitIfExists("graphical.target");
                    }
                    else
                    {
                        if (ServiceManager.Registry.GetUnit("graphical.target") != null)
                        {
                            Log.Debug($"--no-active-default passed, this would be the activation transaction:");
                            Log.Debug(ServiceManager.Planner.CreateActivationTransaction("graphical.target")
                                .GenerateTree());
                        }
                    }

                    Log.Info("Starting late platform initialization...");
                    platform_init.LateInitialize();
                    Log.Info("Late platform initialization complete");
                    
                    await ServiceManager.DBusManager.Connect();
                    
                    try 
                    {
                        await System.Threading.Tasks.Task.Delay(Timeout.Infinite, shutdownCts.Token);
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
            Log.Info("Initiating shutdown...");
            try
            {
                DeactivateUnitIfExists("sockets.target");
                DeactivateUnitIfExists("default.target");

                foreach (var unit in ServiceManager.Registry.Units)
                {
                    if (unit.Value is DeviceUnit)
                        continue;

                    if (unit.Value.CurrentState.HasFlag(SharpInit.Units.UnitState.Activating) ||
                        unit.Value.CurrentState.HasFlag(SharpInit.Units.UnitState.Active))
                    {
                        DeactivateUnitIfExists(unit.Value.UnitName);
                    }
                }

                IpcListener.Stop();
                Log.Info("Goodbye!");
            }
            catch
            {

            }
            finally
            {
                Environment.FailFast(null);
            }
        }

        private static void ActivateUnitIfExists(string name)
        {
            if (ServiceManager.Registry.GetUnit(name) != null)
            {
                Log.Info($"Activating {name}...");
                var tx = LateBoundUnitActivationTask.CreateActivationTransaction(name, "Main thread");

                var exec = ServiceManager.Runner.Register(tx).Enqueue();
                exec.Wait();

                if (tx.GeneratedTransaction != null)
                {
                    // Log.Debug($"This is the transaction:");
                    // Log.Debug(tx.GeneratedTransaction.GenerateTree());
                }
                else
                {
                    Log.Warn($"Failed to generate late-bound activation transaction for {name}: {tx.Execution.Result}.");
                }

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
                var tx = LateBoundUnitActivationTask.CreateDeactivationTransaction(name, "Main thread");

                var exec = ServiceManager.Runner.Register(tx).Enqueue();
                exec.Wait();

                if (tx.GeneratedTransaction != null)
                {
                    //Log.Debug($"This is the transaction:");
                    //Log.Debug(tx.GeneratedTransaction.GenerateTree());
                }
                else
                {
                    Log.Warn($"Failed to generate late-bound deactivation transaction for {name}.");
                }
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
