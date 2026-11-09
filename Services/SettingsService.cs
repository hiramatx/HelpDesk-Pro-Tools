using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using HelpDesk_Pro_Tools.Models;

namespace HelpDesk_Pro_Tools.Services;

/// <summary>Persists user settings to %AppData%\HelpDeskProTools\settings.json.</summary>
public class SettingsService
{
    /// <summary>How many PC names the history keeps.</summary>
    public const int MaxHistory = 30;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "HelpDeskProTools",
        "settings.json");

    public AppSettings Current { get; private set; } = new();

    public SettingsService()
    {
        Load();
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_path))
                Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path)) ?? new AppSettings();

            // The file is hand-editable: keep only names that are valid PC names.
            Current.PcHistory = (Current.PcHistory ?? new())
                .Where(HostName.IsValid)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxHistory)
                .ToList();
        }
        catch
        {
            // Corrupt settings should never stop the app from starting.
            Current = new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            // Write a copy first and swap it in, so a crash mid-write can't leave a half-written file.
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(Current, JsonOptions));
            File.Move(temp, _path, overwrite: true);
        }
        catch
        {
            // Non-critical: history/theme just won't persist.
        }
    }
}
