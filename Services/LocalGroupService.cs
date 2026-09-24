using System;
using System.Collections;
using System.Collections.Generic;
using System.DirectoryServices;
using System.Runtime.InteropServices;

namespace HelpDesk_Pro_Tools.Services;

/// <summary>Lists members of a local group on a remote PC (WinNT provider, same as Computer Management).</summary>
public static class LocalGroupService
{
    // NERR_GroupNotFound
    private const int GroupNotFound = unchecked((int)0x800708AC);

    /// <summary>Comma-separated member list, or null if the group doesn't exist on the PC.</summary>
    public static string? GetMembers(string pc, string group, string? computerName = null)
    {
        using var entry = new DirectoryEntry($"WinNT://{pc}/{group},group");
        try
        {
            _ = entry.NativeObject; // bind now so a missing group is detected here
        }
        catch (COMException ex) when (ex.HResult == GroupNotFound)
        {
            return null;
        }

        var names = new List<string>();
        foreach (var member in (IEnumerable)entry.Invoke("Members")!)
        {
            using var m = new DirectoryEntry(member);
            names.Add(FormatMember(m.Path, computerName ?? pc));
        }

        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names.Count == 0 ? "(none)" : string.Join(", ", names);
    }

    // WinNT://DOMAIN/PC/name -> local account "name"
    // WinNT://PC/name        -> local account "name" (non-domain PC)
    // WinNT://DOMAIN/name    -> domain account "DOMAIN\name"
    // WinNT://S-1-5-21-...   -> orphaned SID, shown as-is
    private static string FormatMember(string path, string computerName)
    {
        var parts = path.Replace("WinNT://", "", StringComparison.OrdinalIgnoreCase).Split('/');
        return parts.Length switch
        {
            >= 3 => parts[^1],
            2 when parts[0].Equals(computerName, StringComparison.OrdinalIgnoreCase) => parts[1],
            2 => $"{parts[0]}\\{parts[1]}",
            _ => parts[0],
        };
    }
}
