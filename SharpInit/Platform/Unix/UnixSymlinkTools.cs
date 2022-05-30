using System;
using System.IO;

using SharpInit;
using SharpInit.Units;
using SharpInit.Tasks;

using Mono.Unix;
using Mono.Unix.Native;

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
            var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var testPath = "";

            foreach (var part in parts)
            {
                testPath += "/" + part;
                
                if (UnixFileSystemInfo.TryGetFileSystemEntry(testPath, out UnixFileSystemInfo entry))
                    if (entry.Exists && entry.IsSymbolicLink)
                        return true;
            }
            
            return false;
        }

        public string ResolveSymlink(string path)
        {
            if (!IsSymlink(path))
                return path;
            
            var basePath = "";
            var parts = Path.GetFullPath(path).Split('/', StringSplitOptions.RemoveEmptyEntries);

            foreach (var part in parts)
            {
                basePath += "/" + part;

                if (UnixFileSystemInfo.TryGetFileSystemEntry(basePath, out UnixFileSystemInfo entry))
                {
                    if (entry.Exists && entry.IsSymbolicLink)
                    {
                        basePath = Path.GetFullPath(GetTarget(basePath), Path.GetDirectoryName(basePath)); 
                    }
                }
            }

            if (path.EndsWith('/'))
                basePath += '/';

            return basePath;
            //
            // var target = GetTarget(path);
            //
            // if (!target.StartsWith("/"))
            // {
            //     target = Path.GetFullPath(target, Path.GetDirectoryName(path));
            // }
            //
            // return ResolveSymlink(target);
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