using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SharpInit.Ipc
{
    /// <summary>
    /// Represents a single message communicated to an IpcInterface.
    /// </summary>
    [JsonObject()]
    public class IpcMessage
    {
        [JsonIgnore]
        public static Random Random = new Random();

        public string SourceName { get; set; }
        public string TargetName { get; set; }
        
        /// <summary>
        /// When performing a method call, Payload holds the arguments, JSON-serialized
        /// When responding to a method call, Payload holds an IpcResult object, JSON-serialized
        /// </summary>
        public byte[] Payload { get; set; }

        [JsonIgnore]
        public string PayloadText { get; set; }

        public int Id { get; set; }
        public bool IsResponse { get; set; }

        public string Endpoint { get; set; }

        public IpcMessage() { }

        public IpcMessage(string source, string target, string endpoint, byte[] payload = null)
        {
            SourceName = source;
            TargetName = target;
            Endpoint = endpoint;

            if(payload != null)
            {
                Payload = payload;
                PayloadText = Encoding.UTF8.GetString(payload);
            }

            Id = Random.Next();
        }

        public IpcMessage(string source, string target, string endpoint, string payload = null) :
            this(source, target, endpoint, payload == null ? null : Encoding.UTF8.GetBytes(payload))
        { }

        public IpcMessage(IpcMessage base_msg, byte[] payload)
        {
            SourceName = base_msg.TargetName;
            TargetName = base_msg.SourceName;
            Endpoint = base_msg.Endpoint;

            if (payload != null)
            {
                Payload = payload;
                PayloadText = Encoding.UTF8.GetString(payload);
            }

            Id = base_msg.Id;
            IsResponse = true;
        }

        public byte[] Serialize()
        {
            return Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(this));
        }

        public static IpcMessage Deserialize(string msg)
        {
            var ret = JsonConvert.DeserializeObject<IpcMessage>(msg, IpcInterface.SerializerSettings);
            var serializer = new JsonSerializer();
            serializer.TypeNameHandling = TypeNameHandling.Auto;
            ret = serializer.Deserialize<IpcMessage>(new JsonTextReader(new StringReader(msg)));

            if (ret.Payload != null)
                ret.PayloadText = Encoding.UTF8.GetString(ret.Payload);

            return ret;
        }
    }
}
