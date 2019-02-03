using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace SharpInit.Units
{
    public static class UnitParser
    {
        private static List<string> TrueAliases = new List<string>() { "true", "yes", "1", "on" };
        private static List<string> FalseAliases = new List<string>() { "false", "no", "0", "off" };

        public static T Parse<T>(string file)
            where T : UnitFile
        {
            var unit = Activator.CreateInstance<T>();

            unit.UnitName = Path.GetFileName(file);
            unit.UnitPath = Path.GetFullPath(file);

            var ext = Path.GetExtension(file);
            ext = ext.TrimStart('.');

            // normalize capitalization
            ext = ext.ToLower();

            if(ext.Length > 1)
                ext = char.ToUpper(ext[0]) + ext.Substring(1);

            var properties = ParseProperties(file);

            foreach (var property in properties)
            {
                var path = property.Key;
                var values = property.Value;

                var name = string.Join("/", path.Split('/').Skip(1));

                if(name.StartsWith("Condition") || name.StartsWith("Assert"))
                {
                    // handle conditions and assertions separately
                    
                    if(name.StartsWith("Condition"))
                    {
                        var condition_name = name.Substring("Condition".Length);

                        if (unit.Conditions.ContainsKey(condition_name))
                            unit.Conditions[condition_name] = unit.Conditions[condition_name].Concat(values).ToList();
                        else
                            unit.Conditions[condition_name] = values.ToList();
                    }
                    else if(name.StartsWith("Assert"))
                    {
                        var assertion_name = name.Substring("Assert".Length);

                        if (unit.Assertions.ContainsKey(assertion_name))
                            unit.Assertions[assertion_name] = unit.Assertions[assertion_name].Concat(values).ToList();
                        else
                            unit.Assertions[assertion_name] = values.ToList();
                    }

                    continue;
                }

                // handle .exec unit paths
                // The execution specific configuration options are configured in the [Service], [Socket], [Mount], or [Swap] sections, depending on the unit type.
                if(path.StartsWith("@"))
                {
                    path = ext + path.Substring(1);
                }

                var prop = ReflectionHelpers.GetClassPropertyInfoByPropertyPath(typeof(T), path);

                if (prop == null) // unknown property
                    continue;     // for now

                var attribute = (UnitPropertyAttribute)prop.GetCustomAttributes(typeof(UnitPropertyAttribute), false)[0];
                var handler_type = attribute.PropertyType;
                var last_value = values.Last();

                switch (handler_type)
                {
                    case UnitPropertyType.String:
                        prop.SetValue(unit, values.Last());
                        break;
                    case UnitPropertyType.Int:
                        if (!int.TryParse(values.Last(), out int prop_val_int))
                            break; // for now
                        prop.SetValue(unit, prop_val_int);
                        break;
                    case UnitPropertyType.Bool:
                        if (TrueAliases.Contains(last_value.ToLower()))
                            prop.SetValue(unit, true);
                        else if (FalseAliases.Contains(last_value.ToLower()))
                            prop.SetValue(unit, false);
                        break;
                    case UnitPropertyType.StringList:
                        prop.SetValue(unit, values);
                        break;
                    case UnitPropertyType.StringListSpaceSeparated:
                        prop.SetValue(unit, values.SelectMany(s => SplitSpaceSeparatedValues(s)).ToList());
                        break;
                }
            }

            // initialize all List<string>s to make our life easier
            var reflection_properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach(var prop in reflection_properties)
            {
                var unit_property_attributes = prop.GetCustomAttributes(typeof(UnitPropertyAttribute), false);

                if (unit_property_attributes.Length == 0)
                    continue;

                if (prop.PropertyType == typeof(List<string>) &&
                    prop.GetValue(unit) == null)
                    prop.SetValue(unit, new List<string>());
            }

            return unit;
        }

        private static Dictionary<string, List<string>> ParseProperties(string path)
        {
            var current_section = "";
            var current_property = "";
            var current_value = "";

            bool escaped_line_break = false;

            var lines = File.ReadAllLines(path);

            if (!string.IsNullOrWhiteSpace(lines.Last()))     // an empty line at the end of the file
                lines = lines.Concat(new[] { "" }).ToArray(); // helps us emit the last property

            var properties = new Dictionary<string, List<string>>();

            foreach (var raw_line in lines)
            {
                var line = raw_line.Trim();

                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    current_section = line.Trim('[', ']');
                    continue;
                }
                else if (line.StartsWith("#") || line.StartsWith(";"))
                {
                    continue;
                }

                var line_parts_by_equals = line.Split('=');
                var consumed = new bool[line_parts_by_equals.Length];
                bool last_part_consumed = false;

                if (!escaped_line_break) // we're starting to define a new property
                {
                    // emit previous property
                    if (current_property != "")
                    {
                        var property_path = $"{current_section}/{current_property}";

                        if (!properties.ContainsKey(property_path))
                            properties[property_path] = new List<string>();

                        properties[property_path].Add(current_value);

                        current_value = "";
                    }

                    current_property = line_parts_by_equals[0];
                    consumed[0] = true;
                }

                for (int i = 0; i < consumed.Length; i++)
                {
                    if (consumed[i])
                    {
                        last_part_consumed = false;
                        continue;
                    }

                    current_value += ((last_part_consumed) ? "=" : "") + line_parts_by_equals[i];
                    consumed[i] = true;
                }
                
                bool quoting = false;
                int comment_start = -1;

                // detect comments
                for (int i = 0; i < current_value.Length; i++)
                {
                    var current_char = current_value[i];

                    switch (current_char)
                    {
                        case '"':
                            quoting = true;
                            break;
                        case '#':
                        case ';':
                            if (quoting)
                                break;

                            comment_start = i;
                            break;
                    }

                    if (comment_start != -1)
                    {
                        current_value = current_value.Substring(0, comment_start);
                        break;
                    }
                }

                current_value = current_value.Trim();
                escaped_line_break = current_value.EndsWith("\\");

                if (escaped_line_break) // escape for next line
                {
                    current_value = current_value.Substring(0, current_value.Length - 1);
                }
            }

            return properties;
        }

        public static List<string> SplitSpaceSeparatedValues(string str)
        {
            var ret = new List<string>();

            var quoting = false;
            string current_run = "";

            for(int i = 0; i < str.Length; i++)
            {
                var current_char = str[i];

                if(quoting && current_char == '"')
                {
                    quoting = false;
                    ret.Add(current_run);
                    current_run = "";
                }
                else if (current_char == '"')
                {
                    quoting = true;
                    ret.Add(current_run.TrimEnd(' '));
                    current_run = "";
                }
                else if (!quoting && current_char == ' ')
                {
                    ret.Add(current_run);
                    current_run = "";
                }
                else
                {
                    current_run += current_char;
                }
            }

            if (!string.IsNullOrWhiteSpace(current_run))
                ret.Add(current_run);

            return ret;
        }
    }
}
