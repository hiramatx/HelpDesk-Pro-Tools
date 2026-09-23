using System;
using System.DirectoryServices;
using System.Linq;
using System.Text;

namespace HelpDesk_Pro_Tools.Services;

/// <summary>Looks up objects in the current user's domain and returns their OU path.</summary>
public static class ActiveDirectoryService
{
    public static string GetComputerOu(string pc)
    {
        // Strip any domain suffix: "PC01.corp.local" -> "PC01"
        var name = pc.Split('.')[0];
        return FindOu($"(&(objectCategory=computer)(cn={Escape(name)}))");
    }

    public static string GetUserOu(string samAccountName)
    {
        return FindOu($"(&(objectCategory=person)(objectClass=user)(sAMAccountName={Escape(samAccountName)}))");
    }

    private static string FindOu(string filter)
    {
        using var searcher = new DirectorySearcher(filter, new[] { "distinguishedName" })
        {
            ClientTimeout = TimeSpan.FromSeconds(10),
        };
        var result = searcher.FindOne();
        if (result is null) return "Not found in Active Directory";

        var dn = result.Properties["distinguishedName"][0]?.ToString() ?? "";
        return ToFriendlyOu(dn);
    }

    /// <summary>"CN=PC01,OU=Laptops,OU=HQ,DC=corp,DC=local" -> "corp.local / HQ / Laptops"</summary>
    private static string ToFriendlyOu(string dn)
    {
        var parts = SplitDn(dn).Skip(1).ToList(); // drop the object's own CN
        var domain = string.Join(".", parts.Where(p => p.StartsWith("DC=", StringComparison.OrdinalIgnoreCase)).Select(p => p[3..]));
        var containers = parts
            .Where(p => !p.StartsWith("DC=", StringComparison.OrdinalIgnoreCase))
            .Select(p => p[(p.IndexOf('=') + 1)..])
            .Reverse();
        return string.Join(" / ", new[] { domain }.Concat(containers));
    }

    // Splits on commas that are not escaped with a backslash.
    private static string[] SplitDn(string dn)
    {
        var parts = new System.Collections.Generic.List<string>();
        var current = new StringBuilder();
        for (var i = 0; i < dn.Length; i++)
        {
            if (dn[i] == '\\' && i + 1 < dn.Length) { current.Append(dn[++i]); continue; }
            if (dn[i] == ',') { parts.Add(current.ToString()); current.Clear(); continue; }
            current.Append(dn[i]);
        }
        parts.Add(current.ToString());
        return parts.ToArray();
    }

    private static string Escape(string value)
    {
        var sb = new StringBuilder();
        foreach (var c in value)
        {
            sb.Append(c switch
            {
                '\\' => @"\5c",
                '*' => @"\2a",
                '(' => @"\28",
                ')' => @"\29",
                '\0' => @"\00",
                _ => c.ToString(),
            });
        }
        return sb.ToString();
    }
}
