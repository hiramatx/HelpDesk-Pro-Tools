using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using HelpDesk_Pro_Tools.Models;

namespace HelpDesk_Pro_Tools.Services;

/// <summary>
/// Builds the Software card from Config\software.json, colouring each version against Config\baselines.json.
/// </summary>
public static class SoftwareInventoryService
{
    public const string CatalogFile = "software.json";
    public const string BaselinesFile = "baselines.json";

    private const int MaxContentBytes = 1024 * 1024; // only search the first 1 MB of a content file
    private const int UninstallReadParallelism = 16;

    private static readonly string[] UninstallKeys =
    {
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
    };

    /// <summary>The process names of the catalog's "process" entries (empty if software.json can't be read).</summary>
    public static IEnumerable<string> ProcessNames()
    {
        try
        {
            return (ConfigFiles.Load<SoftwareCatalog>(CatalogFile)?.Software ?? new())
                .Where(e => !string.IsNullOrWhiteSpace(e.Name) && !string.IsNullOrWhiteSpace(e.Process))
                .Select(e => e.Process!.Trim())
                .ToList();
        }
        catch (Exception)
        {
            return Array.Empty<string>(); // Read reports the problem in the Software card
        }
    }

    public static List<SoftwareResult> Read(string pc, RemoteRegistry? registry, Lazy<RemoteProcessList> processes)
    {
        var catalog = ConfigFiles.Load<SoftwareCatalog>(CatalogFile)
                      ?? throw new FileNotFoundException($"{CatalogFile} not found in {ConfigFiles.Folder}");

        Dictionary<string, string> baselines;
        string? baselineError = null;
        try
        {
            baselines = new Dictionary<string, string>(
                ConfigFiles.Load<Dictionary<string, string>>(BaselinesFile) ?? new(),
                StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            baselines = new(StringComparer.OrdinalIgnoreCase);
            baselineError = ex.Message;
        }

        var uninstall = new Lazy<List<(string Name, string Key)>>(() => ReadUninstallNames(registry));
        var results = new List<SoftwareResult>();

        foreach (var entry in catalog.Software.Where(e => !string.IsNullOrWhiteSpace(e.Name)))
        {
            var name = entry.Name.Trim();
            var label = string.IsNullOrWhiteSpace(entry.Label) ? $"{name} Version" : entry.Label.Trim();
            SoftwareResult result;
            try
            {
                result = string.IsNullOrWhiteSpace(entry.Process)
                    ? Evaluate(name, entry, Find(pc, registry, entry, uninstall), baselines, baselineError)
                    : ReadProcess(name, entry.Process.Trim(), processes.Value);
            }
            catch (Exception ex)
            {
                result = new SoftwareResult(name, "Could not read", FieldStatus.Warn, ex.Message);
            }
            results.Add(result with { Label = label });
        }

        return results;
    }

    // ------------------------------------------------------------------ process check

    /// <summary>"Running / user1, user2" (green) if the process is running, otherwise "NA / NA" (red).</summary>
    private static SoftwareResult ReadProcess(string name, string processName, RemoteProcessList processes)
    {
        var running = processes.Get(processName);
        var count = running.Count;
        var users = running
            .Select(p => p.User)
            .Where(u => !string.IsNullOrEmpty(u))
            .Select(u => u!) // user name without the domain, like the Users card
            .ToList();

        if (count == 0)
            return new SoftwareResult(name, "NA / NA", FieldStatus.Bad, $"{processName} is not running");

        var who = users.Count > 0
            ? string.Join(", ", users.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(u => u, StringComparer.OrdinalIgnoreCase))
            : "Unknown";
        return new SoftwareResult(name, $"Running / {who}", FieldStatus.Ok, $"{processName}: {count} instance(s) running");
    }

    // ------------------------------------------------------------------ finding a version

    /// <summary>What was found: the text to show, the version to compare, or a problem (e.g. a pattern that didn't match).</summary>
    private sealed record Found(string Display, string? Version, string? Problem = null);

    private static Found? Find(string pc, RemoteRegistry? registry, SoftwareEntry entry, Lazy<List<(string Name, string Key)>> uninstall)
    {
        if (entry.BuiltIn is { } builtIn)
        {
            if (!builtIn.Equals("Office", StringComparison.OrdinalIgnoreCase))
                return new Found("Unknown builtIn", null, $"\"{builtIn}\" is not a built-in handler. Only \"Office\" is available.");
            return registry is null ? new Found("Unknown", null, "Remote registry is not available") : ReadOffice(registry);
        }

        foreach (var file in entry.Files ?? Enumerable.Empty<string>())
        {
            var unc = ToRemotePath(pc, file);
            if (!File.Exists(unc)) continue;
            var info = FileVersionInfo.GetVersionInfo(unc);
            var version = (info.ProductVersion ?? info.FileVersion)?.Trim();
            if (!string.IsNullOrEmpty(version)) return new Found(version, version);
        }

        if (entry.Registry is { Key.Length: > 0 } reg && registry?.GetString(reg.Key, reg.Value) is { Length: > 0 } regValue)
            return new Found(regValue.Trim(), regValue.Trim());

        if (entry.Content is { Path.Length: > 0 } content)
        {
            var found = ReadContent(pc, content);
            if (found is not null) return found;
        }

        if (!string.IsNullOrWhiteSpace(entry.Uninstall) && registry is not null)
        {
            var match = uninstall.Value.FirstOrDefault(u => u.Name.StartsWith(entry.Uninstall.Trim(), StringComparison.OrdinalIgnoreCase));
            if (match.Key is not null)
            {
                var version = registry.GetString(match.Key, "DisplayVersion")?.Trim();
                return string.IsNullOrEmpty(version)
                    ? new Found("Installed (no version)", null, $"\"{match.Name}\" has no DisplayVersion in Programs & Features")
                    : new Found(version, version);
            }
        }

        return null; // not installed
    }

    /// <summary>Reads a text file on the PC and returns what the pattern's first ( ) group captured.</summary>
    private static Found? ReadContent(string pc, FileContentRef content)
    {
        var unc = ToRemotePath(pc, content.Path);
        if (!File.Exists(unc)) return null;

        string text;
        using (var stream = new FileStream(unc, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
        {
            var buffer = new char[MaxContentBytes];
            var read = reader.ReadBlock(buffer, 0, buffer.Length);
            text = new string(buffer, 0, read);
        }

        if (string.IsNullOrEmpty(content.Pattern))
            return new Found(text.Trim(), text.Trim());

        Match m;
        try
        {
            m = Regex.Match(text, content.Pattern, RegexOptions.Multiline, TimeSpan.FromSeconds(1));
        }
        catch (ArgumentException ex)
        {
            return new Found("Invalid pattern", null, $"The pattern in software.json is not valid: {ex.Message}");
        }

        if (!m.Success)
            return new Found("Version not found", null, $"The pattern didn't match anything in {content.Path}");

        var value = (m.Groups.Count > 1 ? m.Groups[1].Value : m.Value).Trim();
        return new Found(value, value);
    }

    /// <summary>DisplayName + key path of every Programs & Features entry (read once, only if needed).</summary>
    private static List<(string Name, string Key)> ReadUninstallNames(RemoteRegistry? registry)
    {
        var list = new List<(string, string)>();
        if (registry is not { } reg) return list;

        // Hundreds of keys, one network round trip each: read them in parallel, keeping the registry order.
        foreach (var root in UninstallKeys)
        {
            list.AddRange(reg.GetSubKeyNames(root)
                .AsParallel().AsOrdered().WithDegreeOfParallelism(UninstallReadParallelism)
                .Select(sub => $@"{root}\{sub}")
                .Select(key => (Name: reg.GetString(key, "DisplayName"), Key: key))
                .Where(e => !string.IsNullOrEmpty(e.Name))
                .Select(e => (e.Name!, e.Key))
                .ToList());
        }
        return list;
    }

    /// <summary>"C:\Program Files\x.exe" -> "\\PC\c$\Program Files\x.exe". UNC paths are used as-is.</summary>
    private static string ToRemotePath(string pc, string path)
    {
        path = path.Trim();
        return path.Length >= 3 && path[1] == ':' && path[2] == '\\'
            ? $@"\\{pc}\{char.ToLowerInvariant(path[0])}$\{path[3..]}"
            : path;
    }

    // ------------------------------------------------------------------ colouring

    private static SoftwareResult Evaluate(string name, SoftwareEntry entry, Found? found,
        Dictionary<string, string> baselines, string? baselineError)
    {
        if (found is null)
        {
            return entry.Required
                ? new SoftwareResult(name, "Not installed", FieldStatus.Bad, "Required software is missing")
                : new SoftwareResult(name, "Not installed", FieldStatus.Normal, null);
        }

        if (found.Problem is not null)
            return new SoftwareResult(name, found.Display, FieldStatus.Warn, found.Problem);

        if (!baselines.TryGetValue(name, out var minimum) || string.IsNullOrWhiteSpace(minimum))
        {
            return baselineError is null
                ? new SoftwareResult(name, found.Display, FieldStatus.Normal, null)
                : new SoftwareResult(name, found.Display, FieldStatus.Warn, $"Could not read baselines: {baselineError}");
        }

        var installed = VersionCompare.Parse(found.Version);
        var required = VersionCompare.Parse(minimum);
        if (installed is null)
            return new SoftwareResult(name, found.Display, FieldStatus.Warn, $"\"{found.Version}\" is not a version number that can be compared to the minimum {minimum}");
        if (required is null)
            return new SoftwareResult(name, found.Display, FieldStatus.Warn, $"The minimum \"{minimum}\" in baselines.json is not a version number");

        return VersionCompare.Compare(installed, required) >= 0
            ? new SoftwareResult(name, found.Display, FieldStatus.Ok, $"Up to date (minimum {minimum})")
            : new SoftwareResult(name, found.Display, FieldStatus.Bad, $"Outdated - minimum is {minimum}");
    }

    // ------------------------------------------------------------------ Office (builtIn)

    private static Found? ReadOffice(RemoteRegistry registry)
    {
        // Click-to-Run (Microsoft 365 Apps, Office 2019/2021/2024)
        const string c2r = @"SOFTWARE\Microsoft\Office\ClickToRun\Configuration";
        var version = registry.GetString(c2r, "VersionToReport");
        if (version is not null)
        {
            var products = (registry.GetString(c2r, "ProductReleaseIds") ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(FriendlyOfficeProduct);
            var channel = OfficeChannel(registry.GetString(c2r, "UpdateChannel") ?? registry.GetString(c2r, "CDNBaseUrl"));
            var display = $"{string.Join(" + ", products)} {version}{(channel is null ? "" : $" ({channel})")}".Trim();
            return new Found(display, version);
        }

        // MSI installs
        foreach (var (key, name) in new[] { ("16.0", "Office 2016"), ("15.0", "Office 2013"), ("14.0", "Office 2010") })
        {
            if (registry.GetString($@"SOFTWARE\Microsoft\Office\{key}\Common\InstallRoot", "Path") is not null ||
                registry.GetString($@"SOFTWARE\WOW6432Node\Microsoft\Office\{key}\Common\InstallRoot", "Path") is not null)
                return new Found($"{name} (MSI, {key})", key);
        }

        return null; // not installed
    }

    private static string FriendlyOfficeProduct(string id) => id switch
    {
        "O365ProPlusRetail" or "O365ProPlusEEANoTeamsRetail" => "Microsoft 365 Apps for enterprise",
        "O365BusinessRetail" or "O365BusinessEEANoTeamsRetail" => "Microsoft 365 Apps for business",
        "ProPlus2024Volume" => "Office LTSC Professional Plus 2024",
        "ProPlus2021Volume" => "Office LTSC Professional Plus 2021",
        "ProPlus2019Volume" => "Office Professional Plus 2019",
        _ => id,
    };

    private static string? OfficeChannel(string? url)
    {
        if (url is null) return null;
        if (url.Contains("492350f6-3a01-4f97-b9c0-c7c6ddf67d60", StringComparison.OrdinalIgnoreCase)) return "Current Channel";
        if (url.Contains("55336b82-a18d-4dd6-b5f6-9e5095c314a6", StringComparison.OrdinalIgnoreCase)) return "Monthly Enterprise Channel";
        if (url.Contains("7ffbc6bf-bc32-4f92-8982-f9dd17fd3114", StringComparison.OrdinalIgnoreCase)) return "Semi-Annual Enterprise Channel";
        return null;
    }
}

/// <summary>Compares dotted version numbers segment by segment (so 4.10.2 &gt; 4.9.8).</summary>
public static class VersionCompare
{
    private static readonly Regex Number = new(@"\d+(?:\.\d+)*");

    /// <summary>Takes the first dotted number in the text ("6.4.10 (58326)" -> 6.4.10), or null if there is none.</summary>
    public static long[]? Parse(string? text)
    {
        var m = Number.Match(text ?? "");
        if (!m.Success) return null;
        return m.Value.Split('.').Select(p => long.TryParse(p, out var n) ? n : 0).ToArray();
    }

    public static int Compare(long[] a, long[] b)
    {
        for (var i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            var x = i < a.Length ? a[i] : 0;
            var y = i < b.Length ? b[i] : 0;
            if (x != y) return x.CompareTo(y);
        }
        return 0;
    }
}
