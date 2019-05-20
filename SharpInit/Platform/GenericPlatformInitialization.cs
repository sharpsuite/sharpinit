using System;
using System.Collections.Generic;
using System.Text;

namespace SharpInit.Platform
{
    /// <summary>
    /// Generic platform initialization code that does nothing.
    /// </summary>
    [SupportedOn("generic")]
    public class GenericPlatformInitialization : IPlatformInitialization
    {
        public void Initialize()
        {
            // do nothing
        }
    }
}
