using System;
using System.Collections.Generic;
using System.Text;

namespace SharpInit.Platform.Unix
{
    /// <summary>
    /// Unix platform initialization code. Sets up signals.
    /// </summary>
    [SupportedOn("unix")]
    public class UnixPlatformInitialization : IPlatformInitialization
    {
        public void Initialize()
        {
            SignalHandler.Initialize();
        }
    }
}
