using Newtonsoft.Json;
using System;
using System.Collections.Generic;
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

        public ClientIpcContext(IpcConnection connection, string name)
        {
            Connection = connection;
            SourceName = name;
        }

        public ClientIpcContext() :
            this(new IpcConnection(), "sharpinit-ipc-client")
        {

        }

        public bool ActivateUnit(string name)
        {
            return (bool)InvokeIpcFunction("activate-unit", name).AdditionalData;
        }

        public bool DeactivateUnit(string name)
        {
            return (bool)InvokeIpcFunction("deactivate-unit", name).AdditionalData;
        }

        public bool ReloadUnit(string name)
        {
            return (bool)InvokeIpcFunction("reload-unit", name).AdditionalData;
        }

        public List<string> ListUnits()
        {
            return (List<string>)InvokeIpcFunction("list-units").AdditionalData;
        }

        public bool LoadUnitFromFile(string path)
        {
            return (bool)InvokeIpcFunction("load-unit", path).AdditionalData;
        }

        public bool ReloadUnitFile(string unit)
        {
            return (bool)InvokeIpcFunction("reload-unit", unit).AdditionalData;
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
