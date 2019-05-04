using System;
using System.Collections.Generic;
using System.Text;

namespace SharpInit.Platform
{
    public class ProcessStartInfo
    {
        public string Path { get; set; }
        public string[] Arguments { get; set; }
        public IUserIdentifier User { get; set; }
        public string[] Environment { get; set; }

        public ProcessStartInfo(string path, string[] arguments)
        {
            Path = path;
            Arguments = arguments;
        }
    }
}
