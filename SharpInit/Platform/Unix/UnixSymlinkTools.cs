using System;
using System.IO;

using Mono.Unix;

namespace SharpInit.Platform
{
    [SupportedOn("unix")]
    public class UnixSymlinkTools : ISymlinkTools
    {
        public string GetTarget(string path)
        {
            return new UnixSymbolicLinkInfo(path).ContentsPath;
        }

        public bool IsSymlink(string path)
        {
            return UnixFileSystemInfo.GetFileSystemEntry(path).IsSymbolicLink;
        }
    }
}