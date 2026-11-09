using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Threading.Tasks;

namespace HelpDesk_Pro_Tools.Services;

/// <summary>One running process on the remote PC, as shown in the Process Manager.</summary>
/// <param name="Created">Win32_Process.CreationDate as WMI returned it; with the PID it identifies one process instance.</param>
public sealed record RemoteProcessInfo(
    uint Pid,
    string Name,
    string? Created,
    string? User,
    string? Path,
    string? CommandLine,
    string? Company,
    string? Description,
    ulong WorkingSet,
    bool IsMicrosoftService)
{
    /// <summary>"Host Process for Windows Services · Microsoft Corporation", or whichever of the two is known.</summary>
    public string Details => string.Join(" · ", new[] { Description, Company }.Where(s => !string.IsNullOrWhiteSpace(s)));

    public string PidText => Pid.ToString();

    public string MemoryText => RemoteOperations.FormatBytes((long)Math.Min(WorkingSet, long.MaxValue));

    /// <summary>System Idle Process (0) and System (4) can never be ended.</summary>
    public bool CanEnd => Pid is not (0 or 4);

    public string ToolTip => string.Join(Environment.NewLine,
        new[] { Path, CommandLine is { } c && c != Path && c != $"\"{Path}\"" ? c : null }
            .Where(s => !string.IsNullOrWhiteSpace(s)));
}

/// <summary>
/// Lists and ends processes on a remote PC over WMI (Win32_Process). One instance per window: owners and
/// file details are cached, so a refresh only asks for processes and files it hasn't seen yet.
/// </summary>
public sealed class RemoteProcessManager
{
    private const int Parallelism = 12;

    private readonly string _pc;
    private readonly ConcurrentDictionary<string, (string? Company, string? Description)> _fileInfo = new(StringComparer.OrdinalIgnoreCase);
    private ConcurrentDictionary<(uint Pid, string Created), (string? User, string? Domain)> _owners = new();
    private string? _windowsDir;

    public RemoteProcessManager(string pc) => _pc = pc;

    public List<RemoteProcessInfo> List()
    {
        var scope = WmiHelper.Connect(_pc);

        _windowsDir ??= WmiHelper.Query(scope, "SELECT WindowsDirectory FROM Win32_OperatingSystem")
            .Select(o => o["WindowsDirectory"]?.ToString())
            .FirstOrDefault(d => !string.IsNullOrEmpty(d)) ?? @"C:\Windows";
        var windowsDir = _windowsDir.TrimEnd('\\') + @"\";

        var procs = WmiHelper.Query(scope, "SELECT Handle, ProcessId, Name, CreationDate, ExecutablePath, CommandLine, WorkingSetSize FROM Win32_Process")
            .Cast<ManagementObject>()
            .ToList();

        // File details come over the admin share. When it can't be reached (SMB blocked while WMI works),
        // one check here avoids waiting for a timeout on every exe.
        var shareOk = ShareReachable(windowsDir[0]);

        var owners = new ConcurrentDictionary<(uint, string), (string?, string?)>();
        var result = new ConcurrentBag<RemoteProcessInfo>();
        Parallel.ForEach(procs, new ParallelOptions { MaxDegreeOfParallelism = Parallelism }, proc =>
        {
            var pid = Convert.ToUInt32(proc["ProcessId"] ?? 0u);
            var name = proc["Name"]?.ToString() ?? "";
            var created = proc["CreationDate"]?.ToString();
            var path = proc["ExecutablePath"]?.ToString();
            var commandLine = proc["CommandLine"]?.ToString();
            var workingSet = Convert.ToUInt64(proc["WorkingSetSize"] ?? 0UL);

            var (user, domain) = ReadOwner(proc, pid, created, owners);
            var (company, description) = path is not null && shareOk ? FileInfoOf(path) : (null, null);
            var isMs = IsMicrosoftService(pid, name, path, company, user, domain, windowsDir);
            result.Add(new RemoteProcessInfo(pid, name, created, user, path, commandLine, company, description, workingSet, isMs));
        });

        _owners = owners; // only processes that are still running stay cached
        return result.ToList();
    }

    /// <summary>
    /// Ends a process with Win32_Process.Terminate. Checks the name and start time first, so a PID that
    /// was reused by another process since the list was loaded is not killed.
    /// </summary>
    public void Kill(RemoteProcessInfo target)
    {
        if (!target.CanEnd)
            throw new InvalidOperationException($"{target.Name} (PID {target.Pid}) is part of Windows and can't be ended.");

        var scope = WmiHelper.Connect(_pc);
        var proc = WmiHelper.Query(scope, $"SELECT Handle, Name, CreationDate FROM Win32_Process WHERE ProcessId = {target.Pid}")
            .Cast<ManagementObject>()
            .FirstOrDefault();
        if (proc is null)
            throw new InvalidOperationException($"{target.Name} (PID {target.Pid}) is no longer running.");

        var sameName = string.Equals(proc["Name"]?.ToString(), target.Name, StringComparison.OrdinalIgnoreCase);
        var sameStart = target.Created is null || string.Equals(proc["CreationDate"]?.ToString(), target.Created, StringComparison.Ordinal);
        if (!sameName || !sameStart)
            throw new InvalidOperationException($"PID {target.Pid} now belongs to another process ({proc["Name"]}). Refresh the list and try again.");

        var code = Convert.ToUInt32(proc.InvokeMethod("Terminate", new object[] { 0u }));
        if (code != 0)
            throw new InvalidOperationException($"Could not end {target.Name} (PID {target.Pid}): {TerminateError(code)}.");
    }

    private static string TerminateError(uint code) => code switch
    {
        2 => "access denied",
        3 => "insufficient privilege",
        8 => "unknown failure",
        9 => "path not found",
        21 => "invalid parameter",
        _ => $"error {code}",
    };

    /// <summary>One remote call per process, so owners already read for the same process instance are reused.</summary>
    private (string? User, string? Domain) ReadOwner(ManagementObject proc, uint pid, string? created,
        ConcurrentDictionary<(uint, string), (string?, string?)> seen)
    {
        if (created is null) return WmiHelper.GetOwner(proc);

        var key = (pid, created);
        if (!_owners.TryGetValue(key, out var owner))
            owner = WmiHelper.GetOwner(proc);
        if (owner.User is not null) seen[key] = owner; // a failed lookup is tried again on the next refresh
        return owner;
    }

    private bool ShareReachable(char drive)
    {
        try { return Directory.Exists(RemotePath.AdminShare(_pc, drive)); }
        catch { return false; }
    }

    /// <summary>
    /// Company and description from the exe's version resource, read over the admin share
    /// (C:\Program Files\x.exe → \\PC\c$\Program Files\x.exe). Both null when the file can't be read;
    /// only successful reads are cached, so a file that failed once is tried again on the next refresh.
    /// </summary>
    private (string? Company, string? Description) FileInfoOf(string path)
    {
        if (_fileInfo.TryGetValue(path, out var cached)) return cached;
        if (RemotePath.ToUnc(_pc, path) is not { } unc || unc == path) return (null, null);
        try
        {
            var info = FileVersionInfo.GetVersionInfo(unc);
            return _fileInfo[path] = (Blank(info.CompanyName), Blank(info.FileDescription));
        }
        catch
        {
            return (null, null);
        }

        static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }

    /// <summary>
    /// "Microsoft services" = the background parts of Windows a tech rarely wants to end, the same idea
    /// as "Hide all Microsoft services" in msconfig. A process counts when it is:
    /// - System Idle Process / System (PID 0 and 4), or
    /// - a Microsoft exe (or an exe in the Windows folder whose publisher couldn't be read) that lives in the
    ///   Windows folder or runs as a system account, or
    /// - a protected process whose path can't be read, running as a system account (csrss, lsass, MsMpEng...).
    /// Microsoft apps the user runs from Program Files (Outlook, Teams, Edge) and explorer.exe stay visible.
    /// </summary>
    private static bool IsMicrosoftService(uint pid, string name, string? path, string? company, string? user, string? domain, string windowsDir)
    {
        if (pid is 0 or 4) return true;
        if (name.Equals(RemoteProcessList.Explorer, StringComparison.OrdinalIgnoreCase)) return false;

        var systemAccount = user is null
            || string.Equals(domain, "NT AUTHORITY", StringComparison.OrdinalIgnoreCase)
            || user.StartsWith("DWM-", StringComparison.OrdinalIgnoreCase)
            || user.StartsWith("UMFD-", StringComparison.OrdinalIgnoreCase);

        if (path is null) return systemAccount;

        var inWindows = path.StartsWith(windowsDir, StringComparison.OrdinalIgnoreCase);
        var microsoft = company?.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) ?? inWindows;
        return microsoft && (inWindows || systemAccount);
    }
}
