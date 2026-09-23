namespace HelpDesk_Pro_Tools.Models;

/// <summary>One entry in scripts.json.</summary>
public class ScriptEntry
{
    public string Name { get; set; } = "";

    /// <summary>Absolute path, or relative to the folder containing scripts.json.</summary>
    public string Path { get; set; } = "";

    /// <summary>Optional extra arguments. "{PC}" is replaced with the current remote PC name.</summary>
    public string? Arguments { get; set; }

    public string? Description { get; set; }
}
