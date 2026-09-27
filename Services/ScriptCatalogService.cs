using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HelpDesk_Pro_Tools.Models;

namespace HelpDesk_Pro_Tools.Services;

/// <summary>Loads the script buttons from Config\scripts.json.</summary>
public class ScriptCatalogService
{
    public static readonly string[] DefaultTabs = { "Testing", "Tools", "Fixes", "GAD", "Utilities", "Installs" };

    private const string FileName = "scripts.json";

    public string CatalogPath { get; } = ConfigFiles.PathOf(FileName);

    /// <summary>Returns tabs in the fixed order, followed by any extra categories found in the file.</summary>
    public IReadOnlyList<(string Tab, List<ScriptEntry> Scripts)> Load()
    {
        var data = new Dictionary<string, List<ScriptEntry>>(StringComparer.OrdinalIgnoreCase);

        var parsed = ConfigFiles.Load<Dictionary<string, List<ScriptEntry>>>(FileName);
        if (parsed is not null)
            foreach (var (key, value) in parsed)
                data[key] = value ?? new List<ScriptEntry>();

        // Relative script paths are relative to the exe's folder (where the Scripts folder lives).
        foreach (var entry in data.Values.SelectMany(v => v))
        {
            if (!Path.IsPathRooted(entry.Path))
                entry.Path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, entry.Path));
        }

        return DefaultTabs
            .Concat(data.Keys.Where(k => !DefaultTabs.Contains(k, StringComparer.OrdinalIgnoreCase)))
            .Select(tab => (tab, data.GetValueOrDefault(tab) ?? new List<ScriptEntry>()))
            .ToList();
    }
}
