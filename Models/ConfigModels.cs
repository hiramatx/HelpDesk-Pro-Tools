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

    /// <summary>Process to look for (e.g. "nschill.exe"): shows "Running / user" or "NA / NA".</summary>
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

/// <summary>Config\excluded_users.json</summary>
public class ExcludedUsers
{
    public List<string> AllGroups { get; set; } = new();
    public List<string> LocalAdmins { get; set; } = new();
    public List<string> RemoteDesktopUsers { get; set; } = new();
    public List<string> DirectAccessUsers { get; set; } = new();
}
