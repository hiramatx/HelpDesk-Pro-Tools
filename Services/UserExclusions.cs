using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using HelpDesk_Pro_Tools.Models;

namespace HelpDesk_Pro_Tools.Services;

/// <summary>Hides members listed in Config\excluded_users.json from the PC Details group lists.</summary>
public sealed class UserExclusions
{
    public const string FileName = "excluded_users.json";

    private readonly List<Regex> _all;
    private readonly List<Regex> _localAdmins;
    private readonly List<Regex> _remoteDesktopUsers;
    private readonly List<Regex> _directAccessUsers;

    private UserExclusions(ExcludedUsers config)
    {
        _all = Compile(config.AllGroups);
        _localAdmins = Compile(config.LocalAdmins);
        _remoteDesktopUsers = Compile(config.RemoteDesktopUsers);
        _directAccessUsers = Compile(config.DirectAccessUsers);
    }

    /// <summary>Loads the file; a missing file means nothing is excluded.</summary>
    public static UserExclusions Load() => new(ConfigFiles.Load<ExcludedUsers>(FileName) ?? new ExcludedUsers());

    /// <summary>Excludes nothing (used when the file can't be read).</summary>
    public static UserExclusions None { get; } = new(new ExcludedUsers());

    public IEnumerable<string> FilterLocalAdmins(IEnumerable<string> members) => Filter(members, _localAdmins);
    public IEnumerable<string> FilterRemoteDesktopUsers(IEnumerable<string> members) => Filter(members, _remoteDesktopUsers);
    public IEnumerable<string> FilterDirectAccessUsers(IEnumerable<string> members) => Filter(members, _directAccessUsers);

    private IEnumerable<string> Filter(IEnumerable<string> members, List<Regex> groupPatterns) =>
        members.Where(m => !IsMatch(m, _all) && !IsMatch(m, groupPatterns));

    // A pattern matches the full name ("CORP\jdoe") or just the part after the backslash ("jdoe").
    private static bool IsMatch(string member, List<Regex> patterns)
    {
        var shortName = member.Contains('\\') ? member[(member.LastIndexOf('\\') + 1)..] : member;
        return patterns.Any(p => p.IsMatch(member) || p.IsMatch(shortName));
    }

    private static List<Regex> Compile(IEnumerable<string>? patterns) => Wildcard.Compile(patterns);
}
