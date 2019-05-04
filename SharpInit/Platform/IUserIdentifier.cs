using System;

namespace SharpInit.Platform
{
    public interface IUserIdentifier
    {
        string Username { get; set; }
        string Group { get; set; }
    }
}