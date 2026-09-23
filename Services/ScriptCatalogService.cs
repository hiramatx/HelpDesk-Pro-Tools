using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using HelpDesk_Pro_Tools.Models;

namespace HelpDesk_Pro_Tools.Services;

/// <summary>Loads the script buttons from scripts.json (next to the exe).</summary>
public class ScriptCatalogService
{
    public static readonly string[] DefaultTabs = { "Testing", "Tools", "Fixes", "GAD", "Utilities", "Installs" };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public string CatalogPath { get; } = Path.Combine(AppContext.BaseDirectory, "scripts.json");

    /// <summary>Returns tabs in the fixed order, followed by any extra categories found in the file.</summary>
    public IReadOnlyList<(string Tab, List<ScriptEntry> Scripts)> Load()
    {
        var data = new Dictionary<string, List<ScriptEntry>>(StringComparer.OrdinalIgnoreCase);

        if (File.Exists(CatalogPath))
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, List<ScriptEntry>>>(File.ReadAllText(CatalogPath), JsonOptions);
            if (parsed is not null)
                foreach (var (key, value) in parsed)
                    data[key] = value ?? new List<ScriptEntry>();
        }

        var baseDir = Path.GetDirectoryName(CatalogPath)!;
        foreach (var entry in data.Values.SelectMany(v => v))
        {
            if (!Path.IsPathRooted(entry.Path))
                entry.Path = Path.GetFullPath(Path.Combine(baseDir, entry.Path));
        }

        return DefaultTabs
            .Concat(data.Keys.Where(k => !DefaultTabs.Contains(k, StringComparer.OrdinalIgnoreCase)))
            .Select(tab => (tab, data.GetValueOrDefault(tab) ?? new List<ScriptEntry>()))
            .ToList();
    }
}
