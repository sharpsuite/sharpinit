using System;

namespace SharpInit.Platform
{
    public delegate void OnProcessExit(int pid, int exit_code);

    /// <summary>
    /// Provides a platform-agnostic interface for starting and monitoring processes.
    /// </summary>
    public interface IProcessHandler
    {
        event OnProcessExit ProcessExit;
        ProcessInfo Start(ProcessStartInfo psi);
    }
}