using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using Mono.Unix;
using Mono.Unix.Native;

namespace SharpInit.Platform.Unix
{
    public delegate void OnUnixProcessExit(int pid, int exitcode);
    public static class SignalHandler
    {
        public static event OnUnixProcessExit ProcessExit; 
        static Dictionary<UnixSignal, List<Action>> SignalHandlers = new Dictionary<UnixSignal, List<Action>>();

        public static void Initialize()
        {
            // SIGUSR2 is used to make the .WaitAny call return early when SignalHandlers has been changed
            AddSignalHandler(new UnixSignal(Signum.SIGUSR2), delegate { });
            AddSignalHandler(new UnixSignal(Signum.SIGCHLD), ReapChildren);
            AddSignalHandler(new UnixSignal(Signum.SIGALRM), ReapChildren);
            new Thread((ThreadStart)HandlerLoop).Start();
        }

        static void ReapChildren()
        {
            int pid = 0;
            while ((pid = Syscall.wait(out int status)) > -1)
            {
                ProcessExit?.Invoke(pid, status);
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
                catch { } // this loop should never exit, swallow all exceptions
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
                    return;

                var handlers = SignalHandlers[signal];

                foreach (var handler in handlers)
                {
                    try
                    {
                        handler();
                    }
                    catch { } // swallow all exceptions
                }
            }
            else
                throw new IndexOutOfRangeException();
        }

        public static void AddSignalHandler(UnixSignal signal, Action handler)
        {
            if (!SignalHandlers.ContainsKey(signal))
                SignalHandlers[signal] = new List<Action>();

            SignalHandlers[signal].Add(handler);
            Stdlib.raise(Signum.SIGUSR2);
        }

        public static bool RemoveSignalHandler(UnixSignal signal, Action handler)
        {
            if (!SignalHandlers.ContainsKey(signal))
                return false;

            if (!SignalHandlers[signal].Contains(handler))
                return false;

            SignalHandlers[signal].Remove(handler);
            return true;
        }

        public static void ClearSignalHandlers(UnixSignal signal)
        {
            if (SignalHandlers.ContainsKey(signal))
                SignalHandlers.Remove(signal);
        }
    }
}