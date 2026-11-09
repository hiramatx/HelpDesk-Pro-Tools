using System.Collections.Generic;

namespace HelpDesk_Pro_Tools.Models;

/// <summary>Config\software.json</summary>
public class SoftwareCatalog
{
    public List<SoftwareEntry> Software { get; set; } = new();
}

public class SoftwareEntry
{
    public string Name { get; set; } = "";

    /// <summary>Optional label shown instead of "{Name} Version".</summary>
    public string? Label { get; set; }

    /// <summary>Process to look for (e.g. "agent.exe"): shows "Running / user" or "NA / NA".</summary>
    public string? Process { get; set; }

    public List<string>? Files { get; set; }
    public RegistryValueRef? Registry { get; set; }
    public FileContentRef? Content { get; set; }
    public string? Uninstall { get; set; }
    public string? BuiltIn { get; set; }
    public bool Required { get; set; }
}

public class RegistryValueRef
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}

public class FileContentRef
{
    public string Path { get; set; } = "";
    public string Pattern { get; set; } = "";
}

/// <summary>Config\excluded_video_cards.json</summary>
public class ExcludedVideoCards
{
    public List<string> Excluded { get; set; } = new();
}

/// <summary>Config\excluded_users.json</summary>
public class ExcludedUsers
{
    public List<string> AllGroups { get; set; } = new();
    public List<string> LocalAdmins { get; set; } = new();
    public List<string> RemoteDesktopUsers { get; set; } = new();
    public List<string> DirectAccessUsers { get; set; } = new();
}

/// <summary>One entry in Config\temp_admin_users.json: a user given temporary admin rights on a PC.</summary>
public class TempAdminEntry
{
    /// <summary>When the user was added, "yyyy-MM-dd HH:mm" (local time).</summary>
    public string Date { get; set; } = "";
    public string Pc { get; set; } = "";
    public string User { get; set; } = "";
}

/// <summary>Config\site.json: values that differ per organization. The defaults here apply when the file or a value is missing.</summary>
public class SiteConfig
{
    /// <summary>The DirectAccess group: a local group on the PC, or an AD group if there is no local one.</summary>
    public string DirectAccessGroup { get; set; } = "Direct Access Users";

    /// <summary>IP addresses starting with one of these are shown in orange; empty = no colouring.</summary>
    public List<string> HighlightIpPrefixes { get; set; } = new();

    public double UptimeCautionHours { get; set; } = 48;
    public double UptimeBadHours { get; set; } = 96;
    public int GpoFreshDays { get; set; } = 7;

    /// <summary>Repair source for DISM /RestoreHealth (SFC Scan); empty = Windows Update.</summary>
    public string DismSource { get; set; } = "";

    /// <summary>With a source set: true adds /LimitAccess so DISM doesn't also contact Windows Update.</summary>
    public bool DismLimitAccess { get; set; }

    /// <summary>Tabs of the Scripts card, in order.</summary>
    public List<string> ScriptTabs { get; set; } = new() { "Testing", "Tools", "Fixes", "Utilities", "Installs" };
}
