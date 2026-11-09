using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HelpDesk_Pro_Tools.Models;

namespace HelpDesk_Pro_Tools.Services;

/// <summary>Loads the script buttons from Config\scripts.json.</summary>
public class ScriptCatalogService
{
    /// <summary>The tabs that are always shown, in order (scriptTabs in site.json).</summary>
    public static IReadOnlyList<string> Tabs => SiteSettings.Current.ScriptTabs;

    private const string FileName = "scripts.json";

    public string CatalogPath { get; } = ConfigFiles.PathOf(FileName);

    /// <summary>Returns tabs in the fixed order, followed by any extra categories found in the file.</summary>
    public IReadOnlyList<(string Tab, List<ScriptEntry> Scripts)> Load()
    {
        var data = new Dictionary<string, List<ScriptEntry>>(StringComparer.OrdinalIgnoreCase);

        var parsed = ConfigFiles.Load<Dictionary<string, List<ScriptEntry>>>(FileName);
        if (parsed is not null)
            foreach (var (key, value) in parsed)
                // Entries without a path (or a stray null) are left out instead of failing the whole file.
                data[key] = (value ?? new List<ScriptEntry>())
                    .Where(e => e is not null && !string.IsNullOrWhiteSpace(e.Path))
                    .ToList();

        // Relative script paths are relative to the exe's folder (where the Scripts folder lives).
        foreach (var entry in data.Values.SelectMany(v => v))
        {
            entry.Path = entry.Path.Trim();
            if (!Path.IsPathRooted(entry.Path))
                entry.Path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, entry.Path));
        }

        return Tabs
            .Concat(data.Keys.Where(k => !Tabs.Contains(k, StringComparer.OrdinalIgnoreCase)))
            .Select(tab => (tab, data.GetValueOrDefault(tab) ?? new List<ScriptEntry>()))
            .ToList();
    }
}
