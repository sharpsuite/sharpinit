using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpInit.Ipc
{
    /// <summary>
    /// Static registry that holds a list of the implemented dispatchable functions.
    /// </summary>
    public class IpcFunctionRegistry
    {
        public static List<IpcFunction> Functions = new List<IpcFunction>();

        public static void AddFunction(IpcFunction func)
        {
            Functions.Add(func);
        }

        public static void AddFunction(IEnumerable<IpcFunction> functions)
        {
            Functions.AddRange(functions);
        }

        public static IpcFunction GetFunction(string identifier)
        {
            return Functions.FirstOrDefault(func =>
                func.Identifiers.Any(id => id.Equals(identifier, StringComparison.InvariantCultureIgnoreCase)));
        }
    }
}