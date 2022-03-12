using System;
using System.IO;

using SharpInit;
using SharpInit.Units;
using SharpInit.Tasks;

using Mono.Unix;

namespace SharpInit.Platform
{
    [SupportedOn("unix")]
    public class UnixSymlinkTools : ISymlinkTools
    {
        private ServiceManager ServiceManager { get; set; }

        public UnixSymlinkTools(ServiceManager manager)
        {
            ServiceManager = manager;
        }

        public string GetTarget(string path)
        {
            return new UnixSymbolicLinkInfo(path).ContentsPath;
        }

        public bool IsSymlink(string path)
        {
            if (UnixFileSystemInfo.TryGetFileSystemEntry(path, out UnixFileSystemInfo entry))
                return entry.Exists && entry.IsSymbolicLink;
            
            return false;
        }

        public string ResolveSymlink(string path)
        {
            if (!IsSymlink(path))
                return path;
            
            var target = GetTarget(path);

            if (!target.StartsWith("/"))
            {
                target = Path.GetFullPath(target, Path.GetDirectoryName(path));
            }
            
            return ResolveSymlink(target);
        }

        public bool CreateSymlink(string target, string path, bool symbolic)
        {
            // TODO: Grab a processhandler from a better place
            var proc_handler = ServiceManager.ProcessHandler;
            var args = new string[0];

            if (symbolic)
                args = new [] {"-s", target, path};
            else
                args = new [] {target, path};

            var psi = new ProcessStartInfo("/bin/ln", args);
            psi.Environment = new System.Collections.Generic.Dictionary<string, string>();
            var task = new RunUnregisteredProcessTask(proc_handler, psi, 500);
            var exec = ServiceManager.Runner.Register(task).Enqueue();
            exec.Wait();
            return exec.Result.Type == ResultType.Success;
        }
    }
}