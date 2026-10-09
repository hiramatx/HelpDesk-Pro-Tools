using System;
using System.Collections;
using System.Collections.Generic;
using System.DirectoryServices;
using System.Runtime.InteropServices;

namespace HelpDesk_Pro_Tools.Services;

/// <summary>Lists and changes members of a local group on a remote PC (WinNT provider, same as Computer Management).</summary>
public static class LocalGroupService
{
    // NERR_GroupNotFound
    private const int GroupNotFound = unchecked((int)0x800708AC);
    // ERROR_MEMBER_NOT_IN_ALIAS / ERROR_MEMBER_IN_ALIAS
    private const int MemberNotInGroup = unchecked((int)0x80070561);
    private const int MemberInGroup = unchecked((int)0x80070562);
    // ERROR_NONE_MAPPED / NERR_UserNotFound
    private const int AccountNotFound = unchecked((int)0x80070534);
    private const int UserNotFound = unchecked((int)0x800708AD);

    /// <summary>Member names (sorted), or null if the group doesn't exist on the PC.</summary>
    public static List<string>? GetMembers(string pc, string group, string? computerName = null)
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
        return names;
    }

    /// <summary>
    /// Adds a domain user to a local group on the PC. "jdoe" means the domain this app runs in;
    /// "CORP\jdoe" names the domain. Returns false if the user was already a member.
    /// Throws <see cref="InvalidOperationException"/> with a readable message on failure.
    /// </summary>
    public static bool AddMember(string pc, string group, string user) =>
        Change(pc, group, user, "Add", MemberInGroup);

    /// <summary>Removes a domain user from a local group on the PC. Returns false if the user wasn't a member.</summary>
    public static bool RemoveMember(string pc, string group, string user) =>
        Change(pc, group, user, "Remove", MemberNotInGroup);

    private static bool Change(string pc, string group, string user, string method, int noChangeCode)
    {
        var memberPath = UserPath(user);
        using var entry = new DirectoryEntry($"WinNT://{pc}/{group},group");
        try
        {
            entry.Invoke(method, memberPath);
            return true;
        }
        catch (Exception ex) when (Unwrap(ex) is COMException com)
        {
            if (com.HResult == noChangeCode) return false;
            throw new InvalidOperationException(com.HResult switch
            {
                GroupNotFound => $"The group \"{group}\" doesn't exist on {pc}.",
                AccountNotFound or UserNotFound => $"User \"{user}\" was not found in the domain.",
                _ => com.Message.Trim(),
            }, com);
        }
    }

    /// <summary>"jdoe" or "CORP\jdoe" -> WinNT://CORP/jdoe</summary>
    private static string UserPath(string user)
    {
        var slash = user.IndexOf('\\');
        var (domain, name) = slash > 0
            ? (user[..slash], user[(slash + 1)..])
            : (Environment.UserDomainName, user);
        return $"WinNT://{domain}/{name}";
    }

    // Invoke wraps the COM error in a TargetInvocationException.
    private static Exception Unwrap(Exception ex) =>
        ex is System.Reflection.TargetInvocationException { InnerException: { } inner } ? inner : ex;

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
