using System;
using System.Collections.Generic;
using System.Text;

namespace SharpInit.Units
{
    [AttributeUsage(AttributeTargets.Property)]
    public class UnitPropertyAttribute : Attribute
    {
        public string PropertyPath { get; set; }
        public UnitPropertyType PropertyType { get; set; }

        public Type EnumType { get; set; }

        public object DefaultValue { get; set; }

        public UnitPropertyAttribute(string path, UnitPropertyType type = UnitPropertyType.String, object default_value = null, Type enum_type = null)
        {
            PropertyPath = path;
            PropertyType = type;

            EnumType = enum_type;
            DefaultValue = default_value;
        }
    }

    public enum UnitPropertyType
    {
        String,
        Int,
        Bool,
        StringList,
        StringListSpaceSeparated,
        Enum
    }
}
