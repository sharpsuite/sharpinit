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

        public static string Unescape(string input, bool path = false)
        {
            input = input.Replace('-', '/');

            StringBuilder new_output = new StringBuilder();

            for (int i = 0; i < input.Length; i++)
            {
                var original_char = input[i];
                new_output.Append(original_char);
            }

            return new_output.ToString();
        }
    }
}
