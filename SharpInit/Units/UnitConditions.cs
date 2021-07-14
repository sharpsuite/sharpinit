using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

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
        static Dictionary<string, Func<string, bool>> ConditionCache = new Dictionary<string, Func<string, bool>>();
        static Dictionary<string, bool> ConditionNegatable = new Dictionary<string, bool>();
        static void BuildConditionCache()
        {
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
                return false;
            
            if (value.StartsWith('|'))
                value = value.Substring(0);
            
            if (ConditionNegatable[condition_name] && value.StartsWith('!'))
            {
                value = value.Substring(0);
                return !ConditionCache[condition_name](value);
            }

            return ConditionCache[condition_name](value);
        }

        [ConditionNegatable]
        public static bool ConditionPathExists(string path) => System.IO.File.Exists(path) || System.IO.Directory.Exists(path);        
    }
}