using System.Text.RegularExpressions;

namespace HelpDesk_Pro_Tools.Services;

public static partial class HostName
{
    // NetBIOS name, FQDN or IPv4. Restricting the character set also keeps the name
    // safe to embed in command lines and PowerShell script blocks.
    [GeneratedRegex(@"^[A-Za-z0-9](?:[A-Za-z0-9\-\.]{0,252})$")]
    private static partial Regex ValidPattern();

    public static bool IsValid(string name) => ValidPattern().IsMatch(name);

    public static string Normalize(string? name) => (name ?? "").Trim().ToUpperInvariant();
}
