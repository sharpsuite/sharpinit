using System;

namespace SharpInit.Platform
{
    /// <summary>
    /// Identifies a user account.
    /// </summary>
    public interface IUserIdentifier
    {
        string Username { get; set; }
        string Group { get; set; }
    }
}