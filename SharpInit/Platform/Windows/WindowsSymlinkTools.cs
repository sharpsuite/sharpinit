using System;
using System.IO;

namespace SharpInit.Platform
{
    [SupportedOn("windows")]
    public class WindowsSymlinkTools : ISymlinkTools
    {
        public string GetTarget(string path)
        {
            throw new NotImplementedException();
        }

        public bool IsSymlink(string path)
        {
            return File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
        }

        public bool CreateSymlink(string target, string path, bool symbolic)
        {
            throw new NotImplementedException();
        }
    }
}