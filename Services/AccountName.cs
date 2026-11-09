using System.Linq;

namespace HelpDesk_Pro_Tools.Services;

/// <summary>Helpers for "jdoe" / "CORP\jdoe" style account names.</summary>
public static class AccountName
{
    // Not allowed in Windows account names; several of them would also change the meaning of a WinNT:// path.
    private const string InvalidChars = "\"/\\[]:;|=,+*?<>";

    /// <summary>"CORP\jdoe" -> "jdoe"</summary>
    public static string WithoutDomain(string name) =>
        name.Contains('\\') ? name[(name.LastIndexOf('\\') + 1)..] : name;

    /// <summary>True for "jdoe" or "DOMAIN\jdoe" made of characters Windows allows in account names.</summary>
    public static bool IsValid(string? account)
    {
        if (string.IsNullOrWhiteSpace(account) || account.Length > 256) return false;
        var parts = account.Split('\\');
        return parts.Length <= 2 && parts.All(IsValidPart);
    }

    private static bool IsValidPart(string part) =>
        part.Trim().Length > 0 && !part.Any(c => char.IsControl(c) || InvalidChars.Contains(c));
}
