using SharpInit.Units;
using SharpInit.Platform.Unix;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

using NLog;

namespace SharpInit.Platform
{
    public class ProcessStartInfo
    {
        static Logger Log = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// The path to the executable.
        /// </summary>
        public string Path { get; set; }

        /// <summary>
        /// The command line arguments to be passed to the executed process.
        /// </summary>
        public string[] Arguments { get; set; }

        /// <summary>
        /// Identifies the user to execute the process as.
        /// </summary>
        public IUserIdentifier User { get; set; }

        /// <summary>
        /// An array of environment variables to be passed to the executed process.
        /// </summary>
        public Dictionary<string, string> Environment { get; set; } = new();

        /// <summary>
        /// An array of environment variables to unset before executing the process.
        /// </summary>
        public List<string> UnsetEnvironment { get; set; } = new();

        /// <summary>
        /// Sets the working directory of the executed process.
        /// </summary>
        public string WorkingDirectory { get; set; }

        /// <summary>
        /// Controls where standard input stream of the executed process is connected to.
        /// </summary>
        public string StandardInputTarget { get; set; }

        /// <summary>
        /// Controls where standard output stream of the executed process is connected to.
        /// </summary>
        public string StandardOutputTarget { get; set; }

        /// <summary>
        /// Controls where standard error stream of the executed process is connected to.
        /// </summary>
        public string StandardErrorTarget { get; set; }

        public Unit Unit { get; set; }

        public TimeSpan Timeout { get; set; }

        public bool WaitUntilExec { get; set; }

        public CGroup CGroup { get; set; }
        public bool DelegateCGroupAfterLaunch { get; set; }

        public ProcessStartInfo()
        {
            // these are the only ones supported so far
            StandardInputTarget = "null";
            StandardOutputTarget = "null";
            StandardErrorTarget = "null";
            Environment = new Dictionary<string, string>();
            Arguments = new string[0];
        }

        public ProcessStartInfo(string path, string[] arguments = null, IUserIdentifier user = null, Dictionary<string, string> environment = null, string working_dir = null) 
            : this()
        {
            Path = path;
            Arguments = arguments;
            User = user;
            Environment = environment;
            WorkingDirectory = working_dir;
        }
        
        /// <summary>
        /// Parses a systemd-style command line and returns a ProcessStartInfo
        /// </summary>
        /// <remarks>Prefixes such as ! and + are not supported yet.</remarks>
        /// <param name="cmdline">The command line to parse.</param>
        /// <param name="working_dir">Optional working directory information.</param>
        /// <param name="user">The user to execute the command line under.</param>
        /// <returns></returns>
        public static ProcessStartInfo FromCommandLine(string cmdline, Unit unit = null, TimeSpan timeout = default)
        {
            var orig_cmdline = cmdline;
            timeout = timeout == default ? TimeSpan.MaxValue : timeout;

            // Handle (for now ignore) command line prefixes
            var modifier_chars = new [] { '-', '@', '!', ':', '+' };
            var modifier = "";

            if (modifier_chars.Any(cmdline.StartsWith))
            {
                modifier += cmdline[0];
                cmdline = cmdline.Substring(1);

                // Handle !!
                if (cmdline[0] == '!')
                {
                    modifier += '!';
                    cmdline = cmdline.Substring(1);
                }
            }

            var parts = UnitParser.SplitSpaceSeparatedValues(cmdline);

            var filename = parts[0];
            var args_pre = parts.Skip(1).ToArray();
            var args = new List<string>();

            ProcessStartInfo psi = new ProcessStartInfo();
            psi.Path = filename;
            psi.Unit = unit;
            psi.Timeout = timeout;
            psi.Environment = new Dictionary<string, string>();
            psi.UnsetEnvironment = new();

            var current_env = System.Environment.GetEnvironmentVariables();
            var descriptor_as_exec = (unit.Descriptor is ExecUnitDescriptor exec) ? exec : null;

            if (descriptor_as_exec != null)
            {
                foreach (var key in descriptor_as_exec.PassEnvironment)
                {
                    if (current_env.Contains(key))
                        psi.Environment[key] = current_env[key].ToString();
                }
                
                foreach (var env_set in descriptor_as_exec.Environment)
                {
                    if (!env_set.Contains('='))
                        continue;

                    var env_parts = env_set.Split('=');
                    psi.Environment[env_parts[0]] = string.Join('=', env_parts.Skip(1));
                }

                foreach (var env_file in descriptor_as_exec.EnvironmentFile)
                {
                    bool suppress_failure = false;
                    var path = env_file;

                    if (path.StartsWith('-'))
                    {
                        suppress_failure = true;
                        path = path.Substring(1);
                    }

                    if (suppress_failure && !File.Exists(path))
                        continue;

                    try
                    {
                        var properties = UnitParser.ParseEnvironmentFile(File.ReadAllText(path));

                        foreach (var pair in properties)
                        {
                            foreach (var val in pair.Value)
                            {
                                psi.Environment[pair.Key] = pair.Value;
                            }
                        }
                    }
                    catch { if (!suppress_failure) { throw; } }
                }

                foreach (var env in descriptor_as_exec.UnsetEnvironment)
                {
                    if (!env.Contains('='))
                    {
                        if (psi.Environment.ContainsKey(env))
                            psi.Environment.Remove(env);
                        
                        psi.UnsetEnvironment.Add(env);
                    }
                    else
                    {
                        var key_pos = env.IndexOf('=');
                        var key = env.Substring(0, key_pos);
                        var val = env.Substring(key_pos + 1);

                        if (psi.Environment.ContainsKey(key) && psi.Environment[key] == val)
                            psi.Environment.Remove(key);
                        
                        psi.UnsetEnvironment.Add(key);
                    }
                }
            }
            
            // Expand environment
            foreach (var arg in args_pre)
            {
                // TODO: Handle ${var} in the middle of argument words
                if (arg.StartsWith('$') && (arg.Length > 1 ? arg[1] != '$' : true))
                {
                    string var_name = "";
                    bool split_words = false;

                    if (arg[1] == '{' && arg[arg.Length - 1] == '}')
                    {
                        split_words = false;
                        var_name = arg.Substring(2, arg.Length - 2);
                    }
                    else
                    {
                        split_words = true;
                        var_name = arg.Substring(1);
                    }

                    if (psi.Environment?.ContainsKey(var_name) != true)
                    {
                        Log.Warn($"Encountered unknown environment variable {var_name} in cmdline \"{orig_cmdline}\"");
                        args.Add("");
                    }
                    else
                    {
                        var env_val = psi.Environment[var_name];

                        if (split_words)
                        {
                            // TODO: Handle quotes appropriately (test whether SplitSpaceSeparatedValues is appropriate here)
                            args.AddRange(UnitParser.SplitSpaceSeparatedValues(env_val));
                        }
                        else
                            args.Add(env_val);
                    }
                }
                else
                    args.Add(arg);
            }

            psi.Arguments = args.ToArray();

            if (unit != null && unit is ServiceUnit)  
            {
                var descriptor = unit.Descriptor as ServiceUnitDescriptor;

                psi.WorkingDirectory = descriptor.WorkingDirectory;

                if (!string.IsNullOrWhiteSpace(descriptor.User) && !string.IsNullOrWhiteSpace(descriptor.Group))
                {
                    psi.User = PlatformUtilities.GetImplementation<IUserIdentifier>(descriptor.User, descriptor.Group);
                }
                else if (!string.IsNullOrWhiteSpace(descriptor.User))
                {
                    psi.User = PlatformUtilities.GetImplementation<IUserIdentifier>(descriptor.User);
                }
                else if (!string.IsNullOrWhiteSpace(descriptor.Group))
                {
                    psi.User = PlatformUtilities.GetImplementation<IUserIdentifier>(null, descriptor.Group);
                }
                
                psi.StandardInputTarget = descriptor.StandardInput;
                psi.StandardOutputTarget = descriptor.StandardOutput;
                psi.StandardErrorTarget = descriptor.StandardError;
            }

            return psi;
        }
    }
}
