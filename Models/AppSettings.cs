using System.Collections.Generic;

namespace HelpDesk_Pro_Tools.Models;

public class AppSettings
{
    /// <summary>null = follow the Windows theme on first run.</summary>
    public bool? IsDarkTheme { get; set; }

    /// <summary>Most-recently-used remote PC names, newest first.</summary>
    public List<string> PcHistory { get; set; } = new();
}
