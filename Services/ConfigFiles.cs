using System;
using System.IO;
using System.Text.Json;

namespace HelpDesk_Pro_Tools.Services;

/// <summary>JSON config files in the Config folder next to the exe (shared by everyone when run from a file share).</summary>
public static class ConfigFiles
{
    public static string Folder { get; } = Path.Combine(AppContext.BaseDirectory, "Config");

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static string PathOf(string fileName) => Path.Combine(Folder, fileName);

    /// <summary>Reads Config\<paramref name="fileName"/>; returns null if the file doesn't exist. Throws on invalid JSON.</summary>
    public static T? Load<T>(string fileName) where T : class
    {
        var path = PathOf(fileName);
        if (!File.Exists(path)) return null;

        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{fileName}: {ex.Message}", ex);
        }
    }
}
