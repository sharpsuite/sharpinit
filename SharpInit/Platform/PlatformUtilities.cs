using SharpInit.Platform.Unix;
using SharpInit.Platform.Windows;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SharpInit.Platform
{
    public static class PlatformUtilities
    {
        public static Dictionary<Type, List<Type>> TypesPerInterface = new Dictionary<Type, List<Type>>();
        public static Dictionary<Type, List<string>> PlatformsPerType = new Dictionary<Type, List<string>>();

        public static void RegisterImplementations()
        {
            RegisterImplementation(typeof(UnixUserIdentifier));
            RegisterImplementation(typeof(WindowsUserIdentifier));

            RegisterImplementation(typeof(ForkExecProcessHandler));
            RegisterImplementation(typeof(DefaultProcessHandler));
        }

        public static void RegisterImplementation(Type type)
        {
            var implemented_interfaces = type.GetInterfaces();

            // kind of a heuristic
            Type wanted_interface = implemented_interfaces.FirstOrDefault(iface => iface.Namespace.StartsWith("SharpInit.Platform"));

            if (wanted_interface == default(Type))
                throw new Exception();

            if (!TypesPerInterface.ContainsKey(wanted_interface))
                TypesPerInterface[wanted_interface] = new List<Type>();

            TypesPerInterface[wanted_interface].Add(type);

            // get supported platforms
            var attributes = type.GetCustomAttributes(typeof(SupportedOnAttribute), false);

            if (!attributes.Any())
                throw new Exception();

            PlatformsPerType[type] = ((SupportedOnAttribute)attributes.Single()).Platforms;
        }

        public static T GetImplementation<T>(params object[] constructor_params)
        {
            return GetImplementation<T>(PlatformIdentifier.GetPlatformIdentifier(), constructor_params);
        }

        public static T GetImplementation<T>(PlatformIdentifier id, params object[] constructor_params)
        {
            var T_type = typeof(T);

            if (!TypesPerInterface.ContainsKey(T_type))
                throw new ArgumentException();

            var types = TypesPerInterface[T_type];

            // the platform codes are ordered from most specific to least specific
            foreach(var platform in id.PlatformCodes)
            {
                foreach(var type in types)
                {
                    if (PlatformsPerType[type].Contains(platform))
                        return (T)Activator.CreateInstance(type, constructor_params);
                }
            }

            throw new Exception();
        }
    }
}
