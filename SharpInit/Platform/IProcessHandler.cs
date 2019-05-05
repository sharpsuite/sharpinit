using System;

namespace SharpInit.Platform
{
    public delegate void OnProcessExit(int pid, int exit_code);
    public interface IProcessHandler
    {
        event OnProcessExit ProcessExit;
        ProcessInfo Start(ProcessStartInfo psi);
    }
}