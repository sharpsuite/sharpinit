using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

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
                        if (((UnitPropertyAttribute)attribs.First()).PropertyPath == path)
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
