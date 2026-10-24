using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Threading.Tasks;

namespace HelpDesk_Pro_Tools.Services;

/// <summary>One running process on the remote PC, as shown in the Process Manager.</summary>
public sealed record RemoteProcessInfo(
    uint Pid,
    string Name,
    string? User,
    string? Path,
    string? CommandLine,
    string? Company,
    string? Description,
    ulong WorkingSet,
    bool IsMicrosoftService)
{
    /// <summary>"Microsoft Corporation · Host Process for Windows Services", or whichever of the two is known.</summary>
    public string Details => string.Join(" · ", new[] { Description, Company }.Where(s => !string.IsNullOrWhiteSpace(s)));

    public string PidText => Pid.ToString();

    public string MemoryText => WorkingSet switch
    {
        >= 1024UL * 1024 * 1024 => $"{WorkingSet / (1024.0 * 1024 * 1024):0.00} GB",
        >= 1024UL * 1024 => $"{WorkingSet / (1024.0 * 1024):0.0} MB",
        _ => $"{WorkingSet / 1024.0:0} KB",
    };

    public string ToolTip => string.Join(Environment.NewLine,
        new[] { Path, CommandLine is { } c && c != Path && c != $"\"{Path}\"" ? c : null }
            .Where(s => !string.IsNullOrWhiteSpace(s)));
}

/// <summary>
/// Lists and ends processes on a remote PC over WMI (Win32_Process). One instance per window: file
/// details read over \\PC\c$ are cached so a refresh only reads files it hasn't seen yet.
/// </summary>
public sealed class RemoteProcessManager
{
    private readonly string _pc;
    private readonly ConcurrentDictionary<string, (string? Company, string? Description)> _fileInfo = new(StringComparer.OrdinalIgnoreCase);
    private string? _windowsDir;

    public RemoteProcessManager(string pc) => _pc = pc;

    public List<RemoteProcessInfo> List()
    {
        var scope = WmiHelper.Connect(_pc);

        _windowsDir ??= WmiHelper.Query(scope, "SELECT WindowsDirectory FROM Win32_OperatingSystem")
            .Select(o => o["WindowsDirectory"]?.ToString())
            .FirstOrDefault(d => !string.IsNullOrEmpty(d)) ?? @"C:\Windows";
        var windowsDir = _windowsDir.TrimEnd('\\') + @"\";

        var procs = WmiHelper.Query(scope, "SELECT Handle, ProcessId, Name, ExecutablePath, CommandLine, WorkingSetSize FROM Win32_Process")
            .Cast<ManagementObject>()
            .ToList();

        var result = new ConcurrentBag<RemoteProcessInfo>();
        Parallel.ForEach(procs, new ParallelOptions { MaxDegreeOfParallelism = 12 }, proc =>
        {
            var pid = Convert.ToUInt32(proc["ProcessId"] ?? 0u);
            var name = proc["Name"]?.ToString() ?? "";
            var path = proc["ExecutablePath"]?.ToString();
            var commandLine = proc["CommandLine"]?.ToString();
            var workingSet = Convert.ToUInt64(proc["WorkingSetSize"] ?? 0UL);

            string? user = null, domain = null;
            try
            {
                var args = new object[2];
                if (Convert.ToInt32(proc.InvokeMethod("GetOwner", args)) == 0)
                {
                    user = args[0] as string;
                    domain = args[1] as string;
                }
            }
            catch
            {
                // The process ended while listing, or its owner can't be read; leave it blank.
            }

            var (company, description) = path is null ? (null, null) : _fileInfo.GetOrAdd(path, ReadFileInfo);
            var isMs = IsMicrosoftService(pid, name, path, company, user, domain, windowsDir);
            result.Add(new RemoteProcessInfo(pid, name, user, path, commandLine, company, description, workingSet, isMs));
        });

        return result.ToList();
    }

    /// <summary>
    /// Ends a process with Win32_Process.Terminate. Checks the name first, so a PID that was reused
    /// by another process since the list was loaded is not killed.
    /// </summary>
    public void Kill(uint pid, string expectedName)
    {
        var scope = WmiHelper.Connect(_pc);
        var matches = WmiHelper.Query(scope, $"SELECT Handle, Name FROM Win32_Process WHERE ProcessId = {pid}")
            .Cast<ManagementObject>()
            .ToList();
        var proc = matches.FirstOrDefault();
        if (proc is null)
            throw new InvalidOperationException($"{expectedName} (PID {pid}) is no longer running.");
        if (!string.Equals(proc["Name"]?.ToString(), expectedName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"PID {pid} now belongs to {proc["Name"]}, not {expectedName}. Refresh the list and try again.");

        var code = Convert.ToUInt32(proc.InvokeMethod("Terminate", new object[] { 0u }));
        if (code != 0)
            throw new InvalidOperationException($"Could not end {expectedName} (PID {pid}): {TerminateError(code)}.");
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

    /// <summary>
    /// Company and description from the exe's version resource, read over the admin share
    /// (C:\Program Files\x.exe → \\PC\C$\Program Files\x.exe). Null when the file can't be read.
    /// </summary>
    private (string? Company, string? Description) ReadFileInfo(string path)
    {
        if (path.Length < 3 || path[1] != ':' || path[2] != '\\') return (null, null);
        try
        {
            var unc = $@"\\{_pc}\{path[0]}$\{path[3..]}";
            var info = FileVersionInfo.GetVersionInfo(unc);
            return (Blank(info.CompanyName), Blank(info.FileDescription));
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
        if (name.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase)) return false;

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
