using System;
using System.Collections.Generic;
using System.Text;

namespace SharpInit.Platform
{
    /// <summary>
    /// Represents platform-specific initialization code.
    /// </summary>
    public interface IPlatformInitialization
    {
        void Initialize();
        void LateInitialize();
    }
}
