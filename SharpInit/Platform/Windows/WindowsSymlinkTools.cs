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

        public string ResolveSymlink(string path)
        {
            if (!IsSymlink(path))
                return path;
            
            var target = GetTarget(path);

            if (!Path.IsPathFullyQualified(target))
            {
                target = Path.GetFullPath(target, path);
            }
            
            return ResolveSymlink(target);
        }

        public bool CreateSymlink(string target, string path, bool symbolic)
        {
            throw new NotImplementedException();
        }
    }
}