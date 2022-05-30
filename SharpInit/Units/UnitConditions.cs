using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Mono.Unix.Native;
using SharpInit.Platform;

namespace SharpInit.Units
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    sealed class ConditionNegatableAttribute : System.Attribute
    {
        public ConditionNegatableAttribute()
        {
        }
    }

    public static class UnitConditions
    {
        public static Dictionary<string, List<string>> KernelCommandLine = new Dictionary<string, List<string>>();

        public static Dictionary<string, Func<string, bool>> ConditionCache = new Dictionary<string, Func<string, bool>>();
        static Dictionary<string, bool> ConditionNegatable = new Dictionary<string, bool>();
        public static void BuildConditionCache()
        {
            ConditionCache.Clear();
            ConditionNegatable.Clear();

            var functions = typeof(UnitConditions).GetMethods(BindingFlags.Public | BindingFlags.Static);

            foreach (var function in functions)
            {
                if (function.Name.StartsWith("Condition"))
                {
                    var condition_name = function.Name.Substring("Condition".Length).ToLowerInvariant();

                    if (string.IsNullOrWhiteSpace(condition_name))
                        continue;
                    
                    if (function.GetParameters().SingleOrDefault()?.ParameterType != typeof(string))
                        continue;
                    
                    if (function.ReturnType != typeof(bool))
                        continue;
                    
                    ConditionCache[condition_name] = function.CreateDelegate<Func<string, bool>>();
                    ConditionNegatable[condition_name] = function.GetCustomAttribute(typeof(ConditionNegatableAttribute)) != null;
                }
            }
            
            ParseCommandLine();
        }

        public static bool CheckCondition(string condition_name, string value)
        {
            if (!ConditionCache.Any())
                BuildConditionCache();
            
            condition_name = condition_name.ToLowerInvariant();

            if (condition_name.StartsWith("condition"))
                condition_name = condition_name.Substring("condition".Length);

            if (condition_name.StartsWith("assert"))
                condition_name = condition_name.Substring("assert".Length);

            if (!ConditionCache.ContainsKey(condition_name))
                return true;
            
            if (value.StartsWith('|'))
                value = value.Substring(0);
            
            if (ConditionNegatable[condition_name] && value.StartsWith('!'))
            {
                value = value.Substring(0);
                return !ConditionCache[condition_name](value);
            }

            return ConditionCache[condition_name](value);
        }

        private static void ParseCommandLine()
        {
            if (!PlatformUtilities.CurrentlyOn("unix"))
            {
                return;
            }

            KernelCommandLine.Clear();
            
            var cmdline = File.ReadAllText("/proc/cmdline");
            var split = UnitParser.SplitSpaceSeparatedValues(cmdline);

            foreach (var part in split)
            {
                var key = "";
                string value = null; 
                if (part.Contains('='))
                {
                    key = part.Split('=')[0];
                    value = string.Join('=', part.Split('=').Skip(1));
                }
                else
                {
                    key = part;
                }

                if (!KernelCommandLine.ContainsKey(key))
                    KernelCommandLine[key] = new List<string>();
                KernelCommandLine[key].Add(value);
            }
        }

        [ConditionNegatable]
        public static bool ConditionUser(string user)
        {
            if (uint.TryParse(user, out uint uid))
                return Syscall.getuid() == uid;

            if (user == "@system")
                return Syscall.getuid() == 0;

            return Environment.UserName == user;
        }

        [ConditionNegatable]
        public static bool ConditionPathExists(string path) => System.IO.File.Exists(path) || System.IO.Directory.Exists(path);

        [ConditionNegatable]
        public static bool ConditionKernelCommandLine(string keyword)
        {
            var key_to_search = "";
            string value_to_search = null;

            if (keyword.Contains('='))
            {
                key_to_search = keyword.Split('=')[0];
                value_to_search = string.Join('=', keyword.Split('=').Skip(1));
            }
            else
            {
                key_to_search = keyword;
            }

            if (value_to_search == null)
            {
                return KernelCommandLine.ContainsKey(key_to_search);
            }

            return KernelCommandLine.ContainsKey(key_to_search) &&
                   KernelCommandLine[key_to_search].Contains(value_to_search);
        }
    }
}