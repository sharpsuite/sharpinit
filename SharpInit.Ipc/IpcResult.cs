using System;
using System.Collections.Generic;
using System.Text;

namespace SharpInit.Ipc
{
    /// <summary>
    /// The result of an IPC call.
    /// </summary>
    public class IpcResult
    {
        /// <summary>
        /// Is set to true if no exceptions occurred during the method call.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Holds the return value of the IPC function.
        /// </summary>
        public object AdditionalData { get; set; }

        public IpcResult()
        {

        }

        public IpcResult(bool success, object data = null)
        {
            Success = success;
            AdditionalData = data;
        }
    }
}
