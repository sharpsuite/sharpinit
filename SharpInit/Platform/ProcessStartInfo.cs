using SharpInit.Units;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SharpInit.Platform
{
    public class ProcessStartInfo
    {
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
        public static ProcessStartInfo FromCommandLine(string cmdline, string working_dir, IUserIdentifier user)
        {
            var parts = UnitParser.SplitSpaceSeparatedValues(cmdline);

            var filename = parts[0];
            var args = parts.Skip(1).ToArray();

            ProcessStartInfo psi = new ProcessStartInfo(filename, args, user, null, working_dir);

            return psi;
        }
    }
}
