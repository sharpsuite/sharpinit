using SharpInit.Units;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

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
        public Dictionary<string, string> Environment { get; set; }

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

        public ProcessStartInfo()
        {
            // these are the only ones supported so far
            StandardInputTarget = "null";
            StandardOutputTarget = "null";
            StandardErrorTarget = "null";
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

            var current_env = System.Environment.GetEnvironmentVariables();

            // TODO: Selectively propagate environmental variables
            foreach (var key in current_env.Keys)
                psi.Environment[key.ToString()] = current_env[key].ToString();
            
            if ((unit?.Descriptor is ExecUnitDescriptor) && (unit.Descriptor as ExecUnitDescriptor).Environment?.Count > 0)
            {
                foreach (var env_set in (unit.Descriptor as ExecUnitDescriptor).Environment)
                {
                    if (!env_set.Contains('='))
                        continue;

                    var env_parts = env_set.Split('=');
                    psi.Environment[env_parts[0]] = string.Join('=', env_parts.Skip(1));
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
                            // TODO: Handle quotes appropriately
                            args.AddRange(env_val.Split(' '));
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
                psi.User = (descriptor.Group == null && descriptor.User == null ? null : 
                    PlatformUtilities.GetImplementation<IUserIdentifier>(descriptor.Group, descriptor.User));
                psi.StandardInputTarget = descriptor.StandardInput;
                psi.StandardOutputTarget = descriptor.StandardOutput;
                psi.StandardErrorTarget = descriptor.StandardError;
            }

            return psi;
        }
    }
}
