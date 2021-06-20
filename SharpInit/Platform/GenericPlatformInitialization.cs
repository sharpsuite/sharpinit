using SharpInit.Units;
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
        public virtual void Initialize()
        {
            StringEscaper.BuildAllowedCharacterCache();
            UnitRegistry.InitializeTypes();
            UnitRegistry.CreateBaseUnits();
        }

        public virtual void LateInitialize()
        {

        }
    }
}
