using System;
using System.Collections.Generic;
using System.Text;

namespace SharpInit.Platform.Windows
{
    /// <summary>
    /// Represents a Windows user account.
    /// </summary>
    [SupportedOn("windows")]
    public class WindowsUserIdentifier : IUserIdentifier
    {
        public string Group { get => Domain; set => Domain = value; }

        public string Domain { get; set; }
        public string Username { get; set; }

        public WindowsUserIdentifier(string domain, string username)
        {
            Domain = domain;
            Username = username;
        }
    }
}
