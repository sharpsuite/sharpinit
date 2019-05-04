using System;

namespace SharpInit.Platform
{
    public delegate void OnProcessExit(int pid, int exit_code);
    public interface IProcessHandler
    {
        event OnProcessExit ProcessExit;
        ProcessInfo StartProcess(string path, string[] arguments, string working_dir, IUserIdentifier user);
    }
}