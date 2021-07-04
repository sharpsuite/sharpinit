using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SharpInit
{
    public static class StringEscaper
    {
        public static HashSet<char> AllowedCharacters = new HashSet<char>();

        public static void BuildAllowedCharacterCache()
        {
            AllowedCharacters.Clear();
            AllowedCharacters.Add('-');
            AllowedCharacters.Add(':');
            AllowedCharacters.Add('_');
            AllowedCharacters.Add('.');

            for (char c = 'a'; c <= 'z'; c++)
            {
                AllowedCharacters.Add(c);
                AllowedCharacters.Add(char.ToUpperInvariant(c));
            }

            for (char c = '0'; c <= '9'; c++)
                AllowedCharacters.Add(c);
        }

        public static string Truncate(string input, int length)
        {
            if (input.Length <= length)
                return input;

            length -= 3;            
            
            var start = input.Substring(0, length / 2);
            var end = input.Substring(input.Length - (length / 2), length / 2);

            return start + "..." + end;
        }

        public static string EscapePath(string input) => Escape(input, path: true);

        public static string Escape(string input, bool path = false)
        {
            if (path)
                input = Path.GetFullPath(input);

            StringBuilder new_output = new StringBuilder();
            var last_char = '/';

            if (path && input == "/")
                return "-";

            for(int i = 0; i < input.Length; i++)
            {
                var original_char = input[i];

                if (path)
                    if (original_char == last_char && last_char == '/')
                        continue;

                if (i == 0 && original_char == '.')  // escape '.' if it's the first char in the string
                    new_output.Append($"\\x{Convert.ToString((int)original_char, 16)}");
                else if (original_char == '/')
                    new_output.Append('-');
                else if (!AllowedCharacters.Contains(original_char))
                    new_output.Append($"\\x{Convert.ToString((int)original_char, 16)}");
                else
                    new_output.Append(original_char);
                
                last_char = original_char;
            }

            return new_output.ToString();
        }

        public static string UnescapePath(string input) => Unescape(input, path: true);

        public static string Unescape(string input, bool path = false)
        {
            input = input.Replace('-', '/');

            StringBuilder new_output = new StringBuilder();

            if (path && input[0] != '/')
                new_output.Append('/');

            var backslashes = 0;            

            for (int i = 0; i < input.Length; i++)
            {
                var original_char = input[i];

                if (original_char == '\\')
                    backslashes++;
                else
                    backslashes = 0;
                
                if (backslashes == 1)
                {
                    if (i < (input.Length - 1)) 
                    {
                        var next_char = input[i + 1];

                        if (next_char == 'x')
                        {
                            var next_numbers = "";
                            int j = i + 2;
                            int k = 0;

                            while (j < (input.Length) && k < 2)
                            {
                                var lookahead_char = input[j];
                                if (!char.IsNumber(lookahead_char) && !"abcdef".Contains(char.ToLowerInvariant(lookahead_char)))
                                    break;

                                next_numbers += input[j];
                                j++; k++;
                            }

                            try 
                            {
                                var char_value = Convert.ToInt32(next_numbers, 16);
                                new_output.Append((char)char_value);
                                i = j - 1;
                            }
                            catch { }
                        }
                    }
                }
                else if (backslashes == 2)
                {
                    new_output.Append('\\');
                }
                else if (backslashes > 2)
                {
                    backslashes -= 2;
                }
                else
                {
                    new_output.Append(original_char);
                }
            }

            return new_output.ToString();
        }
    }
}
