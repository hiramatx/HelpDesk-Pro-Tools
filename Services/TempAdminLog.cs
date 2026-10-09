using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using HelpDesk_Pro_Tools.Models;

namespace HelpDesk_Pro_Tools.Services;

/// <summary>
/// Config\temp_admin_users.json: users added as temporary admins, so they can be removed later.
/// The file is created on first use. Every change re-reads it under an exclusive lock, so techs
/// sharing the Config folder on a file share don't overwrite each other's entries.
/// </summary>
public static class TempAdminLog
{
    public const string FileName = "temp_admin_users.json";

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static string FilePath => ConfigFiles.PathOf(FileName);

    /// <summary>All entries, oldest first; empty if the file doesn't exist yet.</summary>
    public static List<TempAdminEntry> Load() => ConfigFiles.Load<List<TempAdminEntry>>(FileName) ?? new();

    public static void Add(string pc, string user) => Update(entries =>
    {
        if (!entries.Any(e => Matches(e, pc, user)))
            entries.Add(new TempAdminEntry { Date = DateTime.Now.ToString("yyyy-MM-dd HH:mm"), Pc = pc, User = user });
    });

    public static void Remove(string pc, string user) => Update(entries => entries.RemoveAll(e => Matches(e, pc, user)));

    private static bool Matches(TempAdminEntry e, string pc, string user) =>
        e.Pc.Equals(pc, StringComparison.OrdinalIgnoreCase) && e.User.Equals(user, StringComparison.OrdinalIgnoreCase);

    private static void Update(Action<List<TempAdminEntry>> change)
    {
        Directory.CreateDirectory(ConfigFiles.Folder);
        using var stream = OpenLocked();

        string text;
        using (var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true))
            text = reader.ReadToEnd();

        List<TempAdminEntry> entries;
        try
        {
            entries = string.IsNullOrWhiteSpace(text)
                ? new()
                : JsonSerializer.Deserialize<List<TempAdminEntry>>(text, ConfigFiles.JsonOptions) ?? new();
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{FileName}: {ex.Message}", ex);
        }

        change(entries);

        stream.SetLength(0);
        stream.Position = 0;
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(JsonSerializer.Serialize(entries, WriteOptions));
    }

    // Another tech may be writing the file right now: wait briefly for their lock to go away.
    private static FileStream OpenLocked()
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return new FileStream(FilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (attempt < 20)
            {
                Thread.Sleep(150);
            }
        }
    }
}
