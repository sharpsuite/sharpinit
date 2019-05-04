using System;
using System.Collections.Generic;
using System.Text;

namespace SharpInit.Platform
{
    [SupportedOn("generic")]
    public class GenericPlatformInitialization : IPlatformInitialization
    {
        public void Initialize()
        {
            // do nothing
        }
    }
}
