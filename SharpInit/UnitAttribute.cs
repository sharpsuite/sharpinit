using System;
using System.Collections.Generic;
using System.Text;

namespace SharpInit
{
    [AttributeUsage(AttributeTargets.Property)]
    public class UnitPropertyAttribute : Attribute
    {
        public string PropertyPath { get; set; }
        public UnitPropertyType PropertyType { get; set; }

        public UnitPropertyAttribute(string path, UnitPropertyType type = UnitPropertyType.String)
        {
            PropertyPath = path;
            PropertyType = type;
        }
    }

    public enum UnitPropertyType
    {
        String,
        Int,
        Bool,
        StringList,
        StringListSpaceSeparated
    }
}
