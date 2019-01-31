using System;
using System.Collections.Generic;
using System.Text;

namespace SharpInit.Units
{
    public class ServiceUnitFile : UnitFile
    {
        public ServiceUnitFile() { }
        public ServiceUnitFile(string path) => UnitParser.Parse<ServiceUnitFile>(path);
    }
}
