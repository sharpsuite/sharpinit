using System;

namespace SharpInit.Platform
{
    /// <summary>
    /// Interface for a symlink manager.
    /// </summary>
    public interface ISymlinkTools
    {
        bool IsSymlink(string path);
        string GetTarget(string path);
    }
}