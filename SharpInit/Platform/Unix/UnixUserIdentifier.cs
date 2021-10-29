using Mono.Unix;
using System;
using System.Collections.Generic;
using System.Text;

namespace SharpInit.Platform.Unix
{
    /// <summary>
    /// Represents a Unix user account.
    /// </summary>
    [SupportedOn("unix")]
    public class UnixUserIdentifier : IUserIdentifier
    {
        public uint UserId { get; set; }
        public uint GroupId { get; set; }

        public string Username { get; set; }
        public string Group { get; set; }

        public UnixUserIdentifier(string username)
        {
            Username = username;

            try
            {
                var user_info = new UnixUserInfo(username);
                UserId = (uint)user_info.UserId;
                GroupId = (uint)user_info.GroupId;
                Group = user_info.GroupName;
            }
            catch { }
        }

        public UnixUserIdentifier(string username, string group)
        {
            Username = username;
            Group = group;

            try
            {
                var user_info = new UnixUserInfo(username);
                UserId = (uint)user_info.UserId;

                if (string.IsNullOrWhiteSpace(group))
                {
                    GroupId = (uint)user_info.GroupId;
                    Group = user_info.GroupName;
                }
                else
                {
                    var group_info = new UnixGroupInfo(group);
                    GroupId = (uint) group_info.GroupId;
                }
            }
            catch { }
        }

        public UnixUserIdentifier(int uid)
        {
            UserId = (uint)uid;
            
            try
            {
                var user_info = new UnixUserInfo(uid);
                Username = user_info.UserName;
                GroupId = (uint)user_info.GroupId;
                Group = user_info.GroupName;
            }
            catch { }
        }

        public UnixUserIdentifier(int uid, int gid)
        {
            UserId = (uint)uid;
            GroupId = (uint)gid;

            try
            {
                var user_info = new UnixUserInfo(uid);
                Username = user_info.UserName;

                var group_info = new UnixGroupInfo(GroupId);
                Group = group_info.GroupName;
            }
            catch { }
        }
    }
}
