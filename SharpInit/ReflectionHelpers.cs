using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using SharpInit.Units;

namespace SharpInit
{
    public static class ReflectionHelpers
    {
        public static PropertyInfo GetClassPropertyInfoByPropertyPath(Type type, string path)
        {
            var prop = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(p =>
                {
                    var attribs = p.GetCustomAttributes(typeof(UnitPropertyAttribute), false);
                    if (attribs.Count() == 1)
                    {
                        var attribute_path = ((UnitPropertyAttribute)attribs.First()).PropertyPath;
                        if (attribute_path.EndsWith('@')) 
                        {
                            attribute_path = attribute_path.Substring(0, attribute_path.Length - 1) + p.Name;
                        }

                        if (attribute_path == path)
                        {
                            return true;
                        }
                    }

                    return false;
                });

            return prop;
        }
    }
}
