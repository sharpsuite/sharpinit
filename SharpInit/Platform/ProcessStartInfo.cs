using SharpInit.Units;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SharpInit.Platform
{
    public class ProcessStartInfo
    {
        public string Path { get; set; }
        public string[] Arguments { get; set; }
        public IUserIdentifier User { get; set; }
        public string[] Environment { get; set; }
        public string WorkingDirectory { get; set; }
        
        public ProcessStartInfo()
        {

        }

        public ProcessStartInfo(string path, string[] arguments = null, IUserIdentifier user = null, string[] environment = null, string working_dir = null)
        {
            Path = path;
            Arguments = arguments;
            User = user;
            Environment = environment;
            WorkingDirectory = working_dir;
        }
        
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
