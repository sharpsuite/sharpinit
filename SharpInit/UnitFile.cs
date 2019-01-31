using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SharpInit
{
    public class UnitFile
    {
        public string UnitPath { get; set; }
        public string UnitName { get; set; }

        [UnitProperty("Unit/Description")]
        public string Description { get; set; }

        [UnitProperty("Unit/Documentation", UnitPropertyType.StringList)]
        public List<string> Documentation { get; set; }

        public UnitFile()
        {

        }

        public static T Parse<T>(string path)
            where T : UnitFile
        {
            var unit = Activator.CreateInstance(typeof(T));

            var current_section = "";
            var current_property = "";
            var current_value = "";

            bool escaped_line_break = false;

            var lines = File.ReadAllLines(path);
            var properties = new Dictionary<string, List<string>>();

            foreach(var raw_line in lines)
            {
                var line = raw_line.Trim();

                if(line.StartsWith("[") && line.EndsWith("]"))
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

                if(!escaped_line_break) // we're starting to define a new property
                {
                    current_property = line_parts_by_equals[0];
                    consumed[0] = true;
                }

                for(int i = 0; i < consumed.Length; i++)
                {
                    if (consumed[i])
                    {
                        last_part_consumed = false;
                        continue;
                    }

                    current_value += ((last_part_consumed) ? "=" : "") + line_parts_by_equals[i];
                    consumed[i] = true;
                }

                if(current_value.EndsWith("\\")) // escape for next line
                {
                    escaped_line_break = true;
                    current_value = current_value.Substring(0, current_value.Length - 1);
                }

                bool quoting = false;
                int comment_start = -1;

                // detect comments
                for(int i = 0; i < current_value.Length; i++)
                {
                    var current_char = current_value[i];

                    switch(current_char)
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
                        current_value = current_value.Substring(comment_start);
                        break;
                    }
                }

                // emit property

                if (!escaped_line_break && current_property != "")
                {
                    var property_path = $"{current_section}/{current_property}";

                    if (!properties.ContainsKey(property_path))
                        properties[property_path] = new List<string>();

                    properties[property_path].Add(current_value);

                    current_value = "";
                }
            }

            return (T)unit;
        }
    }
}
