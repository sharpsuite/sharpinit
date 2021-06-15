using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpInit
{
    public class FileDescriptor
    {
        public int Number { get; set; }
        public string Name { get; set; }
        public int ProcessId { get; set; }

        public FileDescriptor(int id, string name, int pid)
        {
            Number = id;
            Name = name;
            ProcessId = pid;
        }
    }
}
