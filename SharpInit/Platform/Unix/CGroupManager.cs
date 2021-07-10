using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;

using NLog;

namespace SharpInit.Platform.Unix
{
    public class CGroupManager
    {
        public Logger Log = LogManager.GetCurrentClassLogger();

        public string CGroupFSPath = "/sys/fs/cgroup";

        public ServiceManager ServiceManager { get; private set; }

        public CGroup RootCGroup { get; private set; }

        public Dictionary<string, CGroup> CGroups = new Dictionary<string, CGroup>();

        public HashSet<CGroup> WritableCGroups = new HashSet<CGroup>();

        public CGroupManager(ServiceManager manager)
        {
            ServiceManager = manager;
            UpdateRoot();
        }

        public void UpdateRoot(CGroup cgroup = null)
        {
            if (cgroup == null)
                RootCGroup = GetCGroup(File.ReadAllText("/proc/self/cgroup").TrimStart('0', ':').Trim());
            else
                RootCGroup = cgroup;
            
            RootCGroup.Update();
        } 

        public bool CanCreateCGroups() => RootCGroup?.Exists == true && WritableCGroups.Contains(RootCGroup);

        public void MarkCGroupWritable(string cgroup) => MarkCGroupWritable(GetCGroup(cgroup));
        public void MarkCGroupWritable(CGroup cgroup) { WritableCGroups.Add(cgroup); cgroup.MarkWriteable(); }

        private CGroup CreateCGroup(string relative_path, bool path_is_relative = true)
        {
            if (RootCGroup?.Exists != true)
                throw new Exception("Root cgroup does not exist");

            var absolute_path = path_is_relative ? RootCGroup.Path + relative_path : relative_path;
            var cgroup = new CGroup(this, absolute_path);

            if (cgroup.Exists)
                return cgroup;
            
            InitializeCGroup(cgroup);

            return cgroup;
        }

        private void InitializeCGroup(CGroup cgroup)
        {
            var parent_path = Path.GetDirectoryName(cgroup.Path);
            CGroup parent = null;

            if (parent_path == RootCGroup.Path || cgroup == RootCGroup)
                parent = RootCGroup;
            else
                parent = GetCGroup(parent_path, create_if_missing: true);

            if (parent != cgroup)
            {
                InitializeCGroup(parent);
            }

            try 
            {
                if (!cgroup.Exists)
                    Directory.CreateDirectory(cgroup.FileSystemPath);
                
                //if (cgroup.Type != "domain threaded" && cgroup.Type != "threaded")
                    //cgroup.Write("cgroup.type", "threaded");
                
                //foreach (var controller in RootCGroup.AvailableControllers)
                    //cgroup.Write("cgroup.subtree_control", $"+{controller}");
                
                cgroup.Update();
            } 
            catch (Exception ex) 
            { 
                Log.Info(ex, $"Failed to create cgroup {cgroup}"); 
            }

            if (!cgroup.Exists)
                throw new Exception($"Failed to create cgroup {cgroup}");
        }

        private CGroup GetExistingCGroup(string relative_path, bool path_is_relative = true)
        {
            var absolute_path = path_is_relative ? RootCGroup.Path + relative_path : relative_path;
            var cgroup = new CGroup(this, absolute_path);

            if (!cgroup.Exists)
                return null;
            
            return cgroup;
        }

        public CGroup GetCGroup(string absolute_path, bool create_if_missing = true)
        {
            Log.Info($"Asked for cgroup {absolute_path}");

            if (CGroups.ContainsKey(absolute_path))
                return CGroups[absolute_path];
            
            if (Directory.Exists(CGroupFSPath + absolute_path))
                return CGroups[absolute_path] = GetExistingCGroup(absolute_path, path_is_relative: false);

            if (create_if_missing && CanCreateCGroups())
                return CGroups[absolute_path] = CreateCGroup(absolute_path, path_is_relative: false);
            
            return null;
        }

        public bool JoinProcess(CGroup cgroup, int pid)
        {
            if (!cgroup.ManagedByUs)
                return false;

            try
            {
                cgroup.Write(cgroup.Type == "threaded" ? "cgroup.threads" : "cgroup.procs", pid.ToString());
                cgroup.Update();
            }
            catch (Exception ex)
            {
                Log.Warn(ex, $"Exception thrown while joining pid {pid} to cgroup {cgroup}");
            }

            return cgroup.ChildProcesses.Contains(pid);
        }

        internal int Write(CGroup cgroup, string file, string text) => !cgroup.ManagedByUs ? -1 : Write($"{cgroup.FileSystemPath}/{file}", text);
        internal int Write(string path, string text)
        {
            Log.Debug($"{text} > {path}");
            try { File.WriteAllText(path, text); return text.Length; } catch (Exception ex) { Log.Warn(ex); return -1; }

            var fd = Mono.Unix.Native.Syscall.open(path, Mono.Unix.Native.OpenFlags.O_WRONLY | Mono.Unix.Native.OpenFlags.O_TRUNC | Mono.Unix.Native.OpenFlags.O_CREAT, (Mono.Unix.Native.FilePermissions)438);

            if (fd < 0)
                return (int)Mono.Unix.Native.Syscall.GetLastError();

            try 
            {
                text += "\n";

                var bytes = System.Text.Encoding.ASCII.GetBytes(path);
                var buf = System.Runtime.InteropServices.Marshal.AllocHGlobal(bytes.Length);
                System.Runtime.InteropServices.Marshal.Copy(bytes, 0, buf, bytes.Length);

                var written = (int)Mono.Unix.Native.Syscall.write(fd, buf, (ulong)bytes.Length);

                if (written != bytes.Length)
                {
                    Log.Warn($"errno: {Mono.Unix.Native.Syscall.GetLastError()}");
                }
                
                Log.Debug($"Wrote \"{text}\" to {path}: {written} bytes");
                return written;
            }
            catch (Exception ex)
            {
                Log.Warn(ex, $"Failed to write to path {path}");
                return -1;
            }
            finally
            {
                if (fd >= 0)
                    Mono.Unix.Native.Syscall.close(fd);
            }
        }
    }

    // Represents a node on the cgroup tree.
    public class CGroup : IEquatable<CGroup>
    {
        Logger Log = LogManager.GetCurrentClassLogger();
        public CGroupManager Manager { get; private set; }

        public string Path { get; private set; }
        public string RelativePath => Manager?.RootCGroup == null ? Path : ("/" + System.IO.Path.GetRelativePath(Manager.RootCGroup.Path, Path));
        public string FileSystemPath => Manager.CGroupFSPath + Path;
        public bool Exists => Directory.Exists(FileSystemPath);

        private bool _managed_by_us = false;

        public bool ManagedByUs => _managed_by_us || 
            (this != Manager?.RootCGroup ? (((Manager?.RootCGroup?.ManagedByUs ?? false) == false) ? false : Path.StartsWith(Manager.RootCGroup.Path))
                                         : _managed_by_us);

        public List<string> Children { get; private set; }
        public List<int> ChildProcesses { get; private set; }
        public List<string> ChildCGroups { get; private set; }
        public List<string> Controllers { get; private set; }
        public List<string> AvailableControllers { get; private set; }
        public string Type { get; private set; }

        public bool Threaded => Type.Contains("threaded");

        public CGroup(CGroupManager manager, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new Exception($"Invalid cgroup path {path}");
            
            Manager = manager;

            Path = path;

            Children = new List<string>();
            ChildProcesses = new List<int>();
            ChildCGroups = new List<string>();
            Controllers = new List<string>();
            AvailableControllers = new List<string>();

            if (this.Exists && ManagedByUs)
                Update();
        }

        public bool Join(int pid) => Manager.JoinProcess(this, pid);

        public void Update()
        {
            try
            {
                Children.Clear();
                ChildCGroups.Clear();
                ChildProcesses.Clear();

                AvailableControllers.Clear();
                Controllers.Clear();

                Type = File.ReadAllText($"{FileSystemPath}/cgroup.type").Trim();

                ChildProcesses.AddRange(
                    File.ReadAllText($"{FileSystemPath}/{(Threaded ? "cgroup.threads":"cgroup.procs")}")
                    .Split(new [] { ' ', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(pid => int.TryParse(pid, out int _))
                    .Select(int.Parse)
                    .Distinct());
                
                ChildCGroups.AddRange(Directory.GetDirectories(FileSystemPath).Select(d => d.Substring(Manager.CGroupFSPath.Length)));

                Children.AddRange(ChildProcesses.Select(i => i.ToString()).Concat(ChildCGroups));

                AvailableControllers.AddRange(File.ReadAllText($"{FileSystemPath}/cgroup.controllers").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                Controllers.AddRange(File.ReadAllText($"{FileSystemPath}/cgroup.subtree_control").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            }
            catch (Exception ex)
            {
                Log.Warn(ex, $"Failed to parse cgroup info for cgroup {Path}");

                Children.Clear();
                ChildProcesses.Clear();
                ChildCGroups.Clear();
            }
        }

        public string[] Walk()
        {
            Update();

            var lines = new List<string>();

            lines.Add(System.IO.Path.GetFileName(Path));

            foreach (var child_cgroup in ChildCGroups)
            {
                var subtree = Manager.GetCGroup(child_cgroup).Walk();

                //if (subtree.Length == 1 && !ChildProcesses.Any())
                    //lines.Add($"└─{subtree[0]}");
                //else
                {
                    if (child_cgroup == ChildCGroups.Last() && !ChildProcesses.Any())
                    {
                        lines.Add($"└─{subtree[0]}");
                        for (int i = 1; i < subtree.Length; i++)
                            lines.Add($"  {subtree[i]}");
                    }
                    else
                    {
                        lines.Add($"├─{subtree[0]}");
                        for (int i = 1; i < subtree.Length; i++)
                            lines.Add($"│ {subtree[i]}");
                    }
                }
            }

            foreach (var child_pid in ChildProcesses)
            {
                var pipe = child_pid == ChildProcesses.Last() ? "└─" : "├─";
                if (Manager.ServiceManager.ProcessesById.ContainsKey(child_pid))
                    lines.Add($"{pipe}{child_pid} {Manager.ServiceManager.ProcessesById[child_pid].Process.ProcessName}");
                else if (File.Exists($"/proc/{child_pid}/cmdline"))
                {
                    var cmdline = File.ReadAllText($"/proc/{child_pid}/cmdline").Replace('\0', ' ');
                    lines.Add($"{pipe}{child_pid} {cmdline}");
                }
                else
                    lines.Add($"{pipe}{child_pid}");
            }

            return lines.ToArray();
        }

        public int Write(string file, string text) => Manager.Write(this, file, text);

        internal void MarkWriteable() => _managed_by_us = true;

        public override int GetHashCode() => Path.GetHashCode();
        public bool Equals(CGroup cgroup) => Path.Equals(cgroup?.Path);
        public override bool Equals(object obj) => obj is CGroup cgroup && this.Equals(cgroup);

        public override string ToString() => Path;
    }
}