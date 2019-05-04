using System;
using System.Collections.Generic;
using System.Text;

namespace SharpInit.Platform.Unix
{
    [SupportedOn("unix")]
    public class UnixInitialization : IPlatformInitialization
    {
        public void Initialize()
        {
            SignalHandler.Initialize();
        }
    }
}
