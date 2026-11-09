using System;
using System.Collections.Generic;
using System.Linq;
using HelpDesk_Pro_Tools.Models;

namespace HelpDesk_Pro_Tools.Services;

/// <summary>
/// Organization-specific settings from Config\site.json, read once at startup. A missing or invalid
/// file never stops the app: the built-in defaults are used and <see cref="LoadError"/> says why.
/// </summary>
public static class SiteSettings
{
    public const string FileName = "site.json";

    public static SiteConfig Current { get; }

    /// <summary>Why site.json (or one of its values) was not used; null when everything loaded.</summary>
    public static string? LoadError { get; }

    static SiteSettings()
    {
        SiteConfig? loaded = null;
        try
        {
            loaded = ConfigFiles.Load<SiteConfig>(FileName);
        }
        catch (Exception ex)
        {
            LoadError = $"{ex.Message}\n\nThe built-in defaults are used until the file is fixed.";
        }

        var problems = new List<string>();
        Current = Sanitize(loaded ?? new SiteConfig(), problems);
        if (problems.Count > 0)
            LoadError = $"{FileName}: {string.Join(" ", problems)}";
    }

    /// <summary>Replaces missing or unusable values with the defaults, noting each one in <paramref name="problems"/>.</summary>
    private static SiteConfig Sanitize(SiteConfig c, List<string> problems)
    {
        var defaults = new SiteConfig();

        // The group name is used in a WinNT:// path, so it can't contain path separators.
        var group = (c.DirectAccessGroup ?? "").Trim();
        if (group.Length == 0)
        {
            group = defaults.DirectAccessGroup;
        }
        else if (group.IndexOfAny(new[] { '/', '\\', ',' }) >= 0)
        {
            problems.Add($"directAccessGroup \"{group}\" can't contain / \\ or a comma; using \"{defaults.DirectAccessGroup}\".");
            group = defaults.DirectAccessGroup;
        }
        c.DirectAccessGroup = group;

        c.HighlightIpPrefixes = Clean(c.HighlightIpPrefixes);

        if (!(c.UptimeCautionHours > 0)) c.UptimeCautionHours = defaults.UptimeCautionHours;
        if (!(c.UptimeBadHours >= c.UptimeCautionHours))
        {
            problems.Add("uptimeBadHours must not be lower than uptimeCautionHours; using the same value for both.");
            c.UptimeBadHours = c.UptimeCautionHours;
        }

        c.GpoFreshDays = Math.Clamp(c.GpoFreshDays, 1, 3650);

        var source = (c.DismSource ?? "").Trim();
        if (source.Any(char.IsControl) || source.Contains('"'))
        {
            problems.Add("dismSource can't contain quotes or line breaks; it is ignored.");
            source = "";
        }
        c.DismSource = source;

        c.ScriptTabs = Clean(c.ScriptTabs);
        if (c.ScriptTabs.Count == 0) c.ScriptTabs = defaults.ScriptTabs;

        return c;
    }

    private static List<string> Clean(IEnumerable<string>? values) =>
        (values ?? Enumerable.Empty<string>())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
