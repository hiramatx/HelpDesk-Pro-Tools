using System.Collections.Generic;

namespace HelpDesk_Pro_Tools.Models;

public class AppSettings
{
    /// <summary>null = follow the Windows theme on first run.</summary>
    public bool? IsDarkTheme { get; set; }

    /// <summary>Most-recently-used remote PC names, newest first.</summary>
    public List<string> PcHistory { get; set; } = new();

    /// <summary>User name last used in the login box (no password: a kept login is in Windows Credential Manager).</summary>
    public string? LastLoginName { get; set; }

    /// <summary>Last state of the login box's "Keep me logged in" checkbox.</summary>
    public bool KeepLoggedIn { get; set; }
}
