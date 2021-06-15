using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace SharpInit.Ipc
{
    /// <summary>
    /// An implementation of IBaseIpcContext that dispatches serialized method calls over the wire.
    /// </summary>
    public class ClientIpcContext : IBaseIpcContext
    {
        public IpcConnection Connection { get; set; }
        public string SourceName { get; set; }

        private static Dictionary<string, string> FunctionNameToIpcName = new Dictionary<string, string>();

        public ClientIpcContext(IpcConnection connection, string name)
        {
            Connection = connection;
            SourceName = name;

            if (!FunctionNameToIpcName.Any())
            {
                PopulateFunctionNameMappings();
            }
        }

        public ClientIpcContext() :
            this(new IpcConnection(), "sharpinit-ipc-client")
        {

        }

        private void PopulateFunctionNameMappings()
        {
            var type = typeof(IBaseIpcContext);

            foreach (var method in type.GetMethods())
            {
                var attributes = method.GetCustomAttributes(typeof(IpcFunctionAttribute), true);
                if (attributes.Any())
                {
                    FunctionNameToIpcName[method.Name] = (attributes.First() as IpcFunctionAttribute).Name;
                }
            }
        }

        private object[] Wrap(params object[] args) => args;
        public T MakeCall<T>(object[] args = null, [CallerMemberName] string name = null)
        {
            args = args ?? new object[0];
            var method_name = FunctionNameToIpcName[name];
            var result = InvokeIpcFunction(method_name, args);
            if (result.Success)
                return (T)Convert.ChangeType(result.AdditionalData, typeof(T));
            
            return default(T);
        }

        public bool ActivateUnit(string unit) => MakeCall<bool>(Wrap(unit));
        public bool DeactivateUnit(string unit) => MakeCall<bool>(Wrap(unit));
        public bool ReloadUnit(string unit) => MakeCall<bool>(Wrap(unit));
        public List<string> ListUnits() => MakeCall<List<string>>();
        public List<string> ListUnitFiles() => MakeCall<List<string>>();
        public bool LoadUnitFromFile(string path) => MakeCall<bool>(Wrap(path));

        public bool ReloadUnitFile(string unit) => MakeCall<bool>(Wrap(unit));
        public int RescanUnits() => MakeCall<int>();
        public UnitInfo GetUnitInfo(string unit) => MakeCall<UnitInfo>(Wrap(unit));

        public Dictionary<string, List<string>> GetActivationPlan(string unit)
        {
            var result = InvokeIpcFunction("get-activation-plan", unit);
            return result.Success ? (Dictionary<string, List<string>>)result.AdditionalData : null;
        }

        public Dictionary<string, List<string>> GetDeactivationPlan(string unit)
        {
            var result = InvokeIpcFunction("get-deactivation-plan", unit);
            return result.Success ? (Dictionary<string, List<string>>)result.AdditionalData : null;
        }

        public IpcResult InvokeIpcFunction(string name, params object[] args)
        {
            var ipc_message = new IpcMessage(SourceName, "sharpinit", name, 
                JsonConvert.SerializeObject(args, IpcInterface.SerializerSettings));
            var result = Connection.SendMessageWaitForReply(ipc_message);

            return result;
        }
    }
}
