using System;

namespace HelpDesk_Pro_Tools.Services;

/// <summary>Paths on a remote PC, reached through its admin shares (\\PC\c$).</summary>
public static class RemotePath
{
    /// <summary>\\PC\c$</summary>
    public static string AdminShare(string pc, char drive = 'c') => $@"\\{pc}\{char.ToLowerInvariant(drive)}$";

    /// <summary>
    /// "C:\Program Files\x.exe" -> "\\PC\c$\Program Files\x.exe". UNC paths are returned as they are.
    /// Null for anything else (a relative path would otherwise be read from the tech's own PC).
    /// </summary>
    public static string? ToUnc(string pc, string? path)
    {
        path = path?.Trim() ?? "";
        if (path.StartsWith(@"\\", StringComparison.Ordinal)) return path;
        return path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] == '\\'
            ? $@"{AdminShare(pc, path[0])}\{path[3..]}"
            : null;
    }
}
