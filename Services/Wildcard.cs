using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace HelpDesk_Pro_Tools.Services;

/// <summary>Simple name patterns used by the exclusion config files: * is a wildcard, matching ignores case.</summary>
public static class Wildcard
{
    // "svc_*" -> ^svc_.*$ ; everything except * is literal.
    public static List<Regex> Compile(IEnumerable<string>? patterns) =>
        (patterns ?? Enumerable.Empty<string>())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => new Regex("^" + Regex.Escape(p.Trim()).Replace(@"\*", ".*") + "$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            .ToList();

    public static bool IsMatch(string value, List<Regex> patterns) => patterns.Any(p => p.IsMatch(value));
}
