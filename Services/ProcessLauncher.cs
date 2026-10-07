using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HelpDesk_Pro_Tools.Services;

/// <summary>Starts external tools (msra, mstsc, mmc, pwsh, ...).</summary>
public static class ProcessLauncher
{
    /// <summary>Fire-and-forget launch in its own window.</summary>
    public static void Launch(string fileName, string arguments = "")
    {
        Process.Start(new ProcessStartInfo(fileName, arguments) { UseShellExecute = true })?.Dispose();
    }

    /// <summary>
    /// Opens a folder using this app's credentials: File Explorer when the app runs as the logged-on
    /// user, otherwise an in-app file window (see <see cref="FolderBrowser"/> for why).
    /// </summary>
    public static void OpenFolder(string path, Action<string>? onError = null) => FolderBrowser.Open(path, onError);

    /// <summary>Opens a PowerShell 7 console (falls back to Windows PowerShell 5.1).</summary>
    public static void LaunchPowerShell(string arguments)
    {
        try
        {
            Launch("pwsh.exe", arguments);
        }
        catch (Win32Exception)
        {
            Launch("powershell.exe", arguments);
        }
    }

    /// <summary>
    /// Opens Windows Terminal, or a PowerShell console when Terminal isn't available to this account.
    /// </summary>
    /// <remarks>
    /// wt.exe is not a normal program on PATH: it is a per-user App Execution Alias that Windows creates
    /// in %LOCALAPPDATA%\Microsoft\WindowsApps only for accounts that have the Windows Terminal package
    /// registered. When the app runs as a different user (the usual "-admin" account), that account often
    /// never had Terminal provisioned, or the alias is turned off in Settings, so starting "wt.exe" fails
    /// with "The system cannot find the file specified". Look for the alias first and fall back to PowerShell.
    /// </remarks>
    public static void LaunchTerminal()
    {
        var wt = FindWindowsTerminal();
        if (wt is not null)
        {
            try
            {
                Launch(wt);
                return;
            }
            catch (Win32Exception)
            {
                // Alias present but its package isn't usable by this account; use PowerShell instead.
            }
        }

        LaunchPowerShell("-NoLogo");
    }

    /// <summary>Full path of wt.exe for the account this app runs as, or null if it has none.</summary>
    private static string? FindWindowsTerminal()
    {
        var alias = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            @"Microsoft\WindowsApps\wt.exe");
        if (File.Exists(alias)) return alias;

        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            try
            {
                var candidate = Path.Combine(dir.Trim().Trim('"'), "wt.exe");
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException)
            {
                // Malformed PATH entry.
            }
        }

        return null;
    }

    /// <summary>Runs a Scripts-tab .ps1 in Windows PowerShell 5.1 (powershell.exe), in its own console.</summary>
    public static void LaunchScript(string scriptPath, string? extraArgs)
    {
        var powershell = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
        Launch(powershell, $"-NoExit -NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" {extraArgs}".TrimEnd());
    }

    /// <summary>Opens an MMC snap-in, optionally targeted at a remote computer.</summary>
    public static void LaunchMmc(string snapIn, string? computer = null)
    {
        var args = computer is null ? snapIn : $"{snapIn} /computer:\\\\{computer}";
        Launch(Path.Combine(Environment.SystemDirectory, "mmc.exe"), args);
    }

    /// <summary>
    /// Runs a PowerShell script hidden, streaming stdout/stderr lines to <paramref name="log"/>.
    /// Used for jobs that have no pure C# equivalent (e.g. DISM / SFC over WinRM).
    /// </summary>
    public static async Task<int> RunPowerShellAsync(string script, Action<string> log, CancellationToken ct)
    {
        // -EncodedCommand avoids every quoting problem with the script text.
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(
            "[Console]::OutputEncoding = [Text.Encoding]::UTF8\n" + script));
        var args = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}";

        try
        {
            return await RunAndStreamAsync("pwsh.exe", args, log, ct);
        }
        catch (Win32Exception)
        {
            log("pwsh.exe not found, falling back to Windows PowerShell 5.1...");
            return await RunAndStreamAsync("powershell.exe", args, log, ct);
        }
    }

    public static async Task<int> RunAndStreamAsync(string fileName, string arguments, Action<string> log, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => WriteClean(e.Data, log);
        process.ErrorDataReceived += (_, e) => WriteClean(e.Data, log);

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
            throw;
        }

        return process.ExitCode;
    }

    private static void WriteClean(string? line, Action<string> log)
    {
        if (line is null) return;

        // sfc.exe / DISM write UTF-16 with carriage-return progress updates;
        // strip the NULs and keep only the last progress frame.
        line = line.Replace("\0", "");
        var cr = line.LastIndexOf('\r');
        if (cr >= 0 && cr < line.Length - 1) line = line[(cr + 1)..];
        line = line.TrimEnd('\r');

        if (!string.IsNullOrWhiteSpace(line))
            log(line);
    }
}
