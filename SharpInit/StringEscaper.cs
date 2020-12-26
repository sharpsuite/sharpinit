using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SharpInit
{
    public static class StringEscaper
    {
        public static string Escape(string input, bool path = false)
        {
            if (path)
                input = Path.GetFullPath(input);

            input = input.Replace('/', '-');

            StringBuilder new_output = new StringBuilder();

            for(int i = 0; i < input.Length; i++)
            {
                var original_char = input[i];

                if (!char.IsLetterOrDigit(original_char) && original_char != '_' ||
                    (i == 0 && original_char == '.')) // escape '.' if it's the first char in the string
                    new_output.Append($"\\x{Convert.ToString((int)original_char, 16)}");
                else
                    new_output.Append(original_char);
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

                if (!char.IsLetterOrDigit(original_char) && original_char != '_' ||
                    (i == 0 && original_char == '.')) // escape '.' if it's the first char in the string
                    new_output.Append($"\\x{Convert.ToString((int)original_char, 16)}");
                else
                    new_output.Append(original_char);
            }

            return new_output.ToString();
        }
    }
}
