using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SharpInit
{
    public class UnitFile
    {
        public string UnitPath { get; set; }
        public string UnitName { get; set; }

        [UnitProperty("Unit/Description")]
        public string Description { get; set; }

        [UnitProperty("Unit/Documentation", UnitPropertyType.StringListSpaceSeparated)]
        public List<string> Documentation { get; set; }

        [UnitProperty("Unit/After", UnitPropertyType.StringListSpaceSeparated)]
        public List<string> After { get; set; }

        public UnitFile()
        {

        }
    }
}
