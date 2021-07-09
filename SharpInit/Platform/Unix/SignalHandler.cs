using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using Mono.Unix;
using Mono.Unix.Native;

using NLog;

namespace SharpInit.Platform.Unix
{
    public delegate void OnUnixProcessExit(int pid, int exitcode);

    /// <summary>
    /// Handles Unix signals.
    /// </summary>
    public static class SignalHandler
    {
        static Logger Log = LogManager.GetCurrentClassLogger();
        public static event OnUnixProcessExit ProcessExit; 
        static Dictionary<UnixSignal, List<Action>> SignalHandlers = new Dictionary<UnixSignal, List<Action>>();

        /// <summary>
        /// Adds signal handlers, starts the main handling loop.
        /// </summary>
        public static void Initialize()
        {
            // SIGUSR2 is used to make the .WaitAny call return early when SignalHandlers has been changed
            AddSignalHandler(new UnixSignal(Signum.SIGUSR2), delegate { });
            AddSignalHandler(new UnixSignal(Signum.SIGCHLD), ReapChildren);
            AddSignalHandler(new UnixSignal(Signum.SIGALRM), ReapChildren);
            AddSignalHandler(new UnixSignal(Signum.SIGTERM), Program.Shutdown);
            AddSignalHandler(new UnixSignal(Signum.SIGHUP), Program.Shutdown);
            new Thread((ThreadStart)HandlerLoop).Start();
        }

        static void ReapChildren()
        {
            int pid = 0;
            while ((pid = Syscall.wait(out int status)) > -1)
            {
                var signal_bits = status & 0x7f;
                var exit_code = (status & 0xff00) >> 8;
                var stopped = (status & 0xff) == 0x7f;
                var exited = signal_bits == 0;

                if (stopped)
                    Log.Info($"pid {pid} stopped with signal {signal_bits}");

                if (exited)
                    ProcessExit?.Invoke(pid, exit_code);
            }

            Syscall.alarm(60); // thanks sinit, this is neat
        }

        static void HandlerLoop()
        {
            while(true)
            {
                try
                {
                    HandleSignals();
                }
                catch (Exception ex) 
                { 
                    Log.Error(ex, "Exception thrown in signal handler loop");
                } // this loop should never exit, swallow all exceptions
            }
        }

        public static void HandleSignals()
        {
            var copy_of_signals = SignalHandlers.Keys.ToArray();

            int index = UnixSignal.WaitAny(copy_of_signals);

            if (index > 0 && index < copy_of_signals.Length)
            {
                var signal = copy_of_signals[index];

                if (!SignalHandlers.ContainsKey(signal))
                {
                    Log.Warn($"Ignoring signal without handler {signal.Signum}");
                    return;
                }

                var handlers = SignalHandlers[signal];

                foreach (var handler in handlers)
                {
                    try
                    {
                        Log.Debug($"Handling signal {signal.Signum}...");
                        handler();
                    }
                    catch (Exception ex) 
                    { 
                        Log.Warn(ex, $"Exception thrown while handling {signal.Signum}");
                    }
                }
            }
            else
                throw new IndexOutOfRangeException();
        }

        /// <summary>
        /// Adds an Action to be called whenever SharpInit receives the Unix signal <paramref name="signal"/>.
        /// </summary>
        /// <param name="signal">The Unix signal to trigger on.</param>
        /// <param name="handler">The handler to be called whenever we receive the signal.</param>
        public static void AddSignalHandler(UnixSignal signal, Action handler)
        {
            if (!SignalHandlers.ContainsKey(signal))
                SignalHandlers[signal] = new List<Action>();

            SignalHandlers[signal].Add(handler);
            Stdlib.raise(Signum.SIGUSR2);
        }

        /// <summary>
        /// Removes a Unix signal handler.
        /// </summary>
        /// <param name="signal">The Unix signal that <paramref name="handler"/> has been registered under.</param>
        /// <param name="handler">The particular signal handler to remove.</param>
        /// <returns>true if successful.</returns>
        public static bool RemoveSignalHandler(UnixSignal signal, Action handler)
        {
            if (!SignalHandlers.ContainsKey(signal))
                return false;

            if (!SignalHandlers[signal].Contains(handler))
                return false;

            SignalHandlers[signal].Remove(handler);
            return true;
        }

        /// <summary>
        /// Clears all signal handlers for a particular signal.
        /// </summary>
        /// <param name="signal">The Unix signal to clear the handlers of.</param>
        public static void ClearSignalHandlers(UnixSignal signal)
        {
            if (SignalHandlers.ContainsKey(signal))
                SignalHandlers.Remove(signal);
        }
    }
}