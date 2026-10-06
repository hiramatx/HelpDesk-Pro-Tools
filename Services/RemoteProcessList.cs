using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;

namespace HelpDesk_Pro_Tools.Services;

/// <summary>A running process on the remote PC and who owns it (null when GetOwner failed).</summary>
public sealed record RemoteProcess(ManagementObject Process, string? User, string? Domain);

/// <summary>
/// One Win32_Process pass shared by PC Details: explorer.exe (the logged-in users) and the
/// process entries in software.json, each with its owner. Avoids querying Win32_Process once per consumer.
/// </summary>
public sealed class RemoteProcessList
{
    public const string Explorer = "explorer.exe";

    private readonly Dictionary<string, List<RemoteProcess>> _byName;

    private RemoteProcessList(Dictionary<string, List<RemoteProcess>> byName) => _byName = byName;

    public static RemoteProcessList Read(ManagementScope cimv2, IEnumerable<string> processNames)
    {
        var names = processNames
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var byName = new Dictionary<string, List<RemoteProcess>>(StringComparer.OrdinalIgnoreCase);
        if (names.Count == 0) return new RemoteProcessList(byName);

        var where = string.Join(" OR ", names.Select(n => $"Name = '{n.Replace("\\", "\\\\").Replace("'", "\\'")}'"));
        foreach (var proc in WmiHelper.Query(cimv2, $"SELECT Handle, Name FROM Win32_Process WHERE {where}").Cast<ManagementObject>())
        {
            var args = new object[2];
            var ok = Convert.ToInt32(proc.InvokeMethod("GetOwner", args)) == 0;
            var name = proc["Name"]?.ToString() ?? "";
            if (!byName.TryGetValue(name, out var list))
                byName[name] = list = new List<RemoteProcess>();
            list.Add(new RemoteProcess(proc, ok ? args[0] as string : null, ok ? args[1] as string : null));
        }
        return new RemoteProcessList(byName);
    }

    /// <summary>Running instances of <paramref name="name"/> (not case-sensitive); empty if it isn't running.</summary>
    public IReadOnlyList<RemoteProcess> Get(string name) =>
        _byName.TryGetValue(name.Trim(), out var list) ? list : Array.Empty<RemoteProcess>();
}
