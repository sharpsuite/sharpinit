using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace SharpInit.Ipc
{
    /// <summary>
    /// An abstract class that represents an IPC-dispatchable function.
    /// </summary>
    public abstract class IpcFunction
    {
        public abstract string Name { get; }
        public abstract List<string> Identifiers { get; }

        public abstract object Execute(params object[] arguments);
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class IpcFunctionAttribute : Attribute
    {
        public IpcFunctionAttribute()
        {
        }

        public IpcFunctionAttribute(string name) :
            this(name, new[] {name})
        {
        }

        public IpcFunctionAttribute(string[] identifiers) :
            this(identifiers[0], identifiers)
        {
        }

        public IpcFunctionAttribute(string name, string[] identifiers)
        {
            Name = name;
            Identifiers = identifiers.ToArray();
        }

        public string Name { get; set; }
        public string[] Identifiers { get; set; }
    }

    public class DynamicIpcFunction : IpcFunction
    {
        private readonly Delegate _func;

        private DynamicIpcFunction(string name, string[] identifiers, Delegate func)
        {
            Name = name;
            Identifiers = identifiers.ToList();
            _func = func;
        }

        public override string Name { get; }
        public override List<string> Identifiers { get; }

        public override object Execute(params object[] arguments)
        {
            var typeshifted_args = new object[arguments.Length];
            var parameters = _func.Method.GetParameters();

            if (parameters.Length != arguments.Length)
                throw new Exception("Parameter mismatch");

            for (int i = 0; i < arguments.Length; i++)
            {
                typeshifted_args[i] = Convert.ChangeType(arguments[i], parameters[i].ParameterType);
            }

            return _func.DynamicInvoke(typeshifted_args);
        }

        public static DynamicIpcFunction FromAttribute(IpcFunctionAttribute attrib, Delegate func)
        {
            return new DynamicIpcFunction(attrib.Name, attrib.Identifiers.ToArray(), func);
        }

        public static IEnumerable<DynamicIpcFunction> FromContext(IBaseIpcContext context)
        {
            var methods = context.GetType().GetMethods();
            var interface_type = context.GetType().GetInterface("IBaseIpcContext");

            foreach (var method in methods)
            {
                var attribute = (IpcFunctionAttribute) Attribute.GetCustomAttribute(method, typeof(IpcFunctionAttribute));

                if (attribute == null && interface_type != null)
                {
                    var interface_method = interface_type.GetMethod(method.Name);

                    if(interface_method != null)
                        attribute = (IpcFunctionAttribute)Attribute.GetCustomAttribute(interface_method, typeof(IpcFunctionAttribute));
                }

                if (attribute == null)
                    continue;

                if (string.IsNullOrWhiteSpace(attribute.Name)) attribute = new IpcFunctionAttribute(method.Name);
                
                yield return FromAttribute(attribute, method.CreateDelegate(Expression.GetDelegateType(
                    (from parameter in method.GetParameters() select parameter.ParameterType)
                    .Concat(new[] {method.ReturnType})
                    .ToArray()), context));
            }
        }
    }
}