using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

using Mono.Unix.Native;

namespace SharpInit.Units
{
    public static class UnitParser
    {
        private static List<string> TrueAliases = new List<string>() { "true", "yes", "1", "on" };
        private static List<string> FalseAliases = new List<string>() { "false", "no", "0", "off" };

        public static string GetUnitName(string path, bool with_parameter = false)
        {
            if (path[1] == ':' && path[2] == '\\') // detect windows paths
                path = path.Replace('\\', '/');

            var filename = Path.GetFileName(path);
            var filename_without_ext = Path.GetFileNameWithoutExtension(path);

            if (filename_without_ext.Contains("@") && !with_parameter)
                return filename_without_ext.Split('@').First() + "@" + Path.GetExtension(filename);

            return filename;
        }

        public static string GetUnitParameter(string path)
        {
            var filename = Path.GetFileName(path);
            var filename_without_ext = Path.GetFileNameWithoutExtension(path);

            if (filename_without_ext.Contains("@"))
                return string.Join('@', filename_without_ext.Split('@').Skip(1));

            return "";
        }

        public static T FromFiles<T>(params UnitFile[] files)
            where T : UnitDescriptor => (T)FromFiles(typeof(T), files);

        public static UnitDescriptor FromFiles(Type descriptor_type, params UnitFile[] files)
        {
            var descriptor = (UnitDescriptor)Activator.CreateInstance(descriptor_type);
            descriptor.Files = files;

            var properties_touched = new List<PropertyInfo>();

            foreach (var file in files)
            {
                var ext = file.Extension;
                ext = ext.TrimStart('.');

                // normalize capitalization
                ext = ext.ToLower();

                if (ext.Length > 1)
                    ext = char.ToUpper(ext[0]) + ext.Substring(1);

                var properties = file.Properties.ToDictionary(p => p.Key, p => p.Value);

                foreach (var property in properties)
                {
                    var path = property.Key;
                    var values = property.Value;

                    var name = string.Join("/", path.Split('/').Skip(1));

                    var reaggregate_names = new Dictionary<string, Dictionary<string, List<string>>>()
                    {
                        {"Condition", descriptor.Conditions},
                        {"Assert", descriptor.Assertions},
                        {"Listen", descriptor.ListenStatements},
                    };

                    foreach (var reaggregation_pair in reaggregate_names)
                    {
                        if (name.StartsWith(reaggregation_pair.Key))
                        {
                            var trimmed_name = name.Substring(reaggregation_pair.Key.Length);

                            if (reaggregation_pair.Value.ContainsKey(trimmed_name))
                                reaggregation_pair.Value[trimmed_name] = reaggregation_pair.Value[trimmed_name].Concat(values).ToList();
                            else
                                reaggregation_pair.Value[trimmed_name] = values.ToList();
                        }
                    }

                    var prop = ReflectionHelpers.GetClassPropertyInfoByPropertyPath(descriptor_type, path);

                    if (prop == null)
                    {
                        // handle .exec unit paths
                        // The execution specific configuration options are configured in the [Service], [Socket], [Mount], or [Swap] sections, depending on the unit type.
                        if (path.StartsWith(ext + "/", StringComparison.InvariantCultureIgnoreCase))
                        {
                            path = "@" + path.Substring(ext.Length);
                        }

                        prop = ReflectionHelpers.GetClassPropertyInfoByPropertyPath(descriptor_type, path);

                        if (prop == null)
                            continue;
                    }

                    var attribute = (UnitPropertyAttribute)prop.GetCustomAttributes(typeof(UnitPropertyAttribute), false)[0];
                    var handler_type = attribute.PropertyType;
                    var last_value = values.Last();

                    properties_touched.Add(prop);

                    switch (handler_type)
                    {
                        case UnitPropertyType.String:
                            prop.SetValue(descriptor, last_value);
                            break;
                        case UnitPropertyType.Int:
                            if (!int.TryParse(last_value, out int prop_val_int))
                                break; // for now
                            prop.SetValue(descriptor, prop_val_int);
                            break;
                        case UnitPropertyType.IntOctal:
                            try 
                            {
                                prop.SetValue(descriptor, Convert.ToInt32(last_value, 8));
                            }
                            catch {}
                            break;
                        case UnitPropertyType.Bool:
                            if (TrueAliases.Contains(last_value.ToLower()))
                                prop.SetValue(descriptor, true);
                            else if (FalseAliases.Contains(last_value.ToLower()))
                                prop.SetValue(descriptor, false);
                            break;
                        case UnitPropertyType.StringList:
                            if (prop.GetValue(descriptor) == null)
                                prop.SetValue(descriptor, new List<string>());

                            (prop.GetValue(descriptor) as List<string>).AddRange(values);
                            break;
                        case UnitPropertyType.StringListSpaceSeparated:
                            if (prop.GetValue(descriptor) == null)
                                prop.SetValue(descriptor, new List<string>());
                            
                            values = values.SelectMany(s => SplitSpaceSeparatedValues(s)).ToList();
                            (prop.GetValue(descriptor) as List<string>).AddRange(values);
                            break;
                        case UnitPropertyType.Time:
                            prop.SetValue(descriptor, ParseTimeSpan(last_value));
                            break;
                        case UnitPropertyType.Enum:
                            prop.SetValue(descriptor, Enum.Parse(attribute.EnumType, last_value.Replace("-", ""), true));
                            break;
                        case UnitPropertyType.Signal:
                            Mono.Unix.Native.Signum sigval = Mono.Unix.Native.Signum.SIGUNUSED;
                            var signame = last_value.ToUpperInvariant();

                            if (int.TryParse(signame, out int signum))
                            {
                                if (Enum.IsDefined(typeof(Mono.Unix.Native.Signum), signum))
                                {
                                    sigval = (Mono.Unix.Native.Signum)signum;
                                }
                            }

                            if (!signame.StartsWith("SIG"))
                                signame = "SIG" + signame;
                            
                            if (Enum.IsDefined(typeof(Mono.Unix.Native.Signum), signame))
                            {
                                sigval = Enum.Parse<Mono.Unix.Native.Signum>(signame);
                            }

                            if (sigval != Signum.SIGUNUSED)
                            {
                                if (prop.PropertyType == typeof(Signum))
                                    prop.SetValue(descriptor, sigval);
                                else if (prop.PropertyType == typeof(int))
                                    prop.SetValue(descriptor, (int)sigval);
                            }

                            break;
                    }
                }
            }
            
            // initialize default values of unspecified properties
             // also initialize all List<string>s to make our life easier
            var reflection_properties = descriptor_type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var prop in reflection_properties)
            {
                var unit_property_attributes = prop.GetCustomAttributes(typeof(UnitPropertyAttribute), false);

                if (unit_property_attributes.Length == 0)
                    continue;

                var attribute = (UnitPropertyAttribute)unit_property_attributes.FirstOrDefault();

                if (!properties_touched.Contains(prop))
                {
                    if (prop.PropertyType == typeof(List<string>) && attribute.DefaultValue == null)
                        prop.SetValue(descriptor, new List<string>());
                    else if (prop.PropertyType == typeof(TimeSpan) && attribute.DefaultValue is string)
                        prop.SetValue(descriptor, ParseTimeSpan((string)attribute.DefaultValue));
                    else
                        prop.SetValue(descriptor, attribute.DefaultValue);
                }
            }

            return descriptor;
        }

        public static TimeSpan ParseTimeSpan(string str)
        {
            if (double.TryParse(str, out double seconds)) // if the entire string is one number, treat it as the number of seconds
                return TimeSpan.FromSeconds(seconds);
            
            if (string.IsNullOrWhiteSpace(str))
                return TimeSpan.Zero;

            var span = TimeSpan.Zero;

            var zero = DateTime.MinValue;
            var base_date = zero;
            var words = str.Split(' ');

            Dictionary<string, string> mappings = new Dictionary<string, string>()
            {
                {"y", "year" },
                {"m", "minute" },
                {"s", "second" },
                {"d", "day" },
                {"w", "week" },
                {"h", "hour" },
                {"ms", "millisecond" }
            };

            for (int i = 0; i < words.Length; i++)
            {
                double amount = 0;
                string unit = "";
                var word = words[i];

                if (!double.TryParse(word, out amount))
                {
                    var chopped_off = Enumerable.Range(1, word.Length).Reverse().Select(offset =>
                        word.Substring(0, offset)).Where(s => double.TryParse(s, out amount));

                    if (!chopped_off.Any())
                        continue;

                    bool found = false;

                    foreach (var fragment in chopped_off)
                    {
                        var longest = fragment;
                        var possible_unit = word.Substring(longest.Length);

                        if (double.TryParse(longest, out amount) &&
                            mappings.ContainsKey(possible_unit))
                        {
                            unit = mappings[possible_unit];
                            found = true;
                            break;
                        }
                        else
                            continue;
                    }

                    if (!found)
                        continue;
                }

                switch (unit)
                {
                    case "year":
                        while (amount >= 1)
                        {
                            base_date = base_date.AddYears(1);
                            amount -= 1;
                        }

                        base_date = base_date.AddDays(amount * (DateTime.IsLeapYear(base_date.Year) ? 366 : 365));
                        break;
                    case "month":
                        while (amount >= 1)
                        {
                            base_date = base_date.AddMonths(1);
                            amount -= 1;
                        }

                        base_date = base_date.AddDays(amount * DateTime.DaysInMonth(base_date.Year, base_date.Month));
                        break;
                    case "week":
                        base_date = base_date.AddDays(amount * 7);
                        break;
                    case "day":
                        base_date = base_date.AddDays(amount);
                        break;
                    case "hour":
                        base_date = base_date.AddHours(amount);
                        break;
                    case "minute":
                        base_date = base_date.AddMinutes(amount);
                        break;
                    case "second":
                        base_date = base_date.AddSeconds(amount);
                        break;
                    case "millisecond":
                        base_date = base_date.AddMilliseconds(amount);
                        break;
                }
            }

            return base_date - zero;
        }

        public static OnDiskUnitFile ParseFile(string path)
        {
            return File.Exists(path) ? new OnDiskUnitFile(path)
            {
                Properties = ParseProperties(path)
            } : null;
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

                    current_property = line_parts_by_equals[0].Trim();
                    consumed[0] = true;
                }

                for (int i = 1; i < line_parts_by_equals.Length; i++)
                {
                    current_value += ((i > 1) ? "=" : "") + line_parts_by_equals[i];
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

        public static List<KeyValuePair<string, string>> ParseEnvironmentFile(string contents)
        {
            var properties = new List<KeyValuePair<string, string>>();
            var lines = contents.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Concat(new [] { "" }); // emit last line

            var continuing_line = "";

            foreach (var l in lines)
            {
                var line = l;

                if (line.StartsWith('#') || line.StartsWith(';'))
                    continue;
                
                if (line.EndsWith('\\'))
                {
                    line = line.Substring(0, line.Length - 1);

                    if (!line.Contains('"') && !continuing_line.Contains('"')) // rough heuristic
                        line = line.TrimStart();
                    
                    continuing_line += line;
                    continue;
                }
                else if (continuing_line.Any())
                {
                    line = continuing_line + line;
                    continuing_line = "";
                }

                var key_length = line.IndexOf('=');

                if (key_length == -1)
                    continue;

                var key = line.Substring(0, key_length);
                var value = line.Substring(key_length + 1);

                value = value.Trim();
                value = value.Trim('"');

                properties.Add(new KeyValuePair<string, string>(key, value));
            }

            return properties;
        }

        public static List<string> SplitSpaceSeparatedValues(string str)
        {
            var ret = new List<string>();

            var quoting = false;
            string current_run = "";
            char quote_char = ' ';

            for(int i = 0; i < str.Length; i++)
            {
                var current_char = str[i];

                if(quoting && current_char == quote_char)
                {
                    quoting = false;

                    if(current_run != "")
                        ret.Add(current_run);

                    current_run = "";
                }
                else if (current_char == '"' || current_char == '\'')
                {
                    quoting = true;
                    quote_char = current_char;

                    if (!string.IsNullOrWhiteSpace(current_run))
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
