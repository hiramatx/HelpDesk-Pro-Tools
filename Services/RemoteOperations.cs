using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

namespace HelpDesk_Pro_Tools.Services;

/// <summary>
/// Remote jobs run against a PC via admin shares, WMI and the remote event log API.
/// Every job reports progress through a log callback and honours cancellation.
/// </summary>
public static class RemoteOperations
{
    private static readonly string[] SkippedProfiles = { "Public", "Default", "Default User", "All Users" };

    // ---------------------------------------------------------------- SFC / DISM

    public static async Task SfcScanAsync(string pc, Action<string> log, CancellationToken ct)
    {
        log($"Running DISM RestoreHealth and SFC on {pc} over WinRM. This can take 15-30 minutes...");
        log("");

        // No managed API exists for DISM/SFC, so this one goes through PowerShell remoting.
        var script = $$"""
            Invoke-Command -ComputerName '{{pc}}' -ErrorAction Stop -ScriptBlock {
                Write-Output '===== DISM /Online /Cleanup-Image /RestoreHealth ====='
                DISM.exe /Online /Cleanup-Image /RestoreHealth
                Write-Output ''
                Write-Output '===== SFC /scannow ====='
                sfc.exe /scannow
            }
            """;

        var exit = await ProcessLauncher.RunPowerShellAsync(script, log, ct);
        log("");
        log(exit == 0 ? "SFC / DISM finished." : $"PowerShell exited with code {exit}.");
    }

    // ---------------------------------------------------------------- Temp cleanup

    public static Task CleanupTempAsync(string pc, Action<string> log, CancellationToken ct) => Task.Run(() =>
    {
        var root = $@"\\{pc}\c$";
        if (!Directory.Exists(root))
            throw new IOException($"Cannot reach {root}. Check the PC is online and the admin share is available.");

        long totalFreed = 0;

        void Clean(string label, string path, bool deleteSubfolders = true)
        {
            ct.ThrowIfCancellationRequested();
            if (!Directory.Exists(path)) return;

            var (freed, skipped) = DeleteContents(path, deleteSubfolders, ct);
            totalFreed += freed;
            log($"  {label,-45} {FormatBytes(freed),10} freed{(skipped > 0 ? $"   ({skipped} in use / skipped)" : "")}");
        }

        log("Windows Temp");
        Clean(@"C:\Windows\Temp", Path.Combine(root, "Windows", "Temp"));

        log("");
        log("Recycle Bin");
        var recycle = Path.Combine(root, "$Recycle.Bin");
        if (Directory.Exists(recycle))
        {
            foreach (var sidFolder in SafeDirs(recycle))
                Clean($"$Recycle.Bin\\{Path.GetFileName(sidFolder)}", sidFolder);
        }

        foreach (var profile in SafeDirs(Path.Combine(root, "Users")))
        {
            var user = Path.GetFileName(profile);
            if (SkippedProfiles.Contains(user, StringComparer.OrdinalIgnoreCase)) continue;

            log("");
            log($"User: {user}");
            var local = Path.Combine(profile, "AppData", "Local");
            Clean("User Temp", Path.Combine(local, "Temp"));

            CleanBrowser("Edge", Path.Combine(local, "Microsoft", "Edge", "User Data"));
            CleanBrowser("Chrome", Path.Combine(local, "Google", "Chrome", "User Data"));
        }

        void CleanBrowser(string browser, string userData)
        {
            if (!Directory.Exists(userData)) return;

            // "Default", "Profile 1", "Profile 2", ...
            foreach (var browserProfile in SafeDirs(userData)
                         .Where(d => Path.GetFileName(d) is "Default" || Path.GetFileName(d).StartsWith("Profile ")))
            {
                var name = Path.GetFileName(browserProfile);
                Clean($"{browser} ({name}) Cache", Path.Combine(browserProfile, "Cache"));
                Clean($"{browser} ({name}) Code Cache", Path.Combine(browserProfile, "Code Cache"));
                Clean($"{browser} ({name}) GPU Cache", Path.Combine(browserProfile, "GPUCache"));
                Clean($"{browser} ({name}) Service Worker Cache", Path.Combine(browserProfile, "Service Worker", "CacheStorage"));
            }
        }

        log("");
        log($"Cleanup complete. Total freed: {FormatBytes(totalFreed)}");
    }, ct);

    private static (long freed, int skipped) DeleteContents(string folder, bool deleteSubfolders, CancellationToken ct)
    {
        long freed = 0;
        int skipped = 0;

        foreach (var file in SafeFiles(folder))
        {
            ct.ThrowIfCancellationRequested();
            if (Path.GetFileName(file).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                var info = new FileInfo(file);
                var size = info.Length;
                if (info.IsReadOnly) info.IsReadOnly = false;
                info.Delete();
                freed += size;
            }
            catch
            {
                skipped++;
            }
        }

        if (!deleteSubfolders) return (freed, skipped);

        foreach (var dir in SafeDirs(folder))
        {
            ct.ThrowIfCancellationRequested();
            var (f, s) = DeleteContents(dir, true, ct);
            freed += f;
            skipped += s;
            try { Directory.Delete(dir, false); } catch { /* not empty (locked files) */ }
        }

        return (freed, skipped);
    }

    private static IEnumerable<string> SafeDirs(string path)
    {
        try { return Directory.GetDirectories(path); }
        catch { return Array.Empty<string>(); }
    }

    private static IEnumerable<string> SafeFiles(string path)
    {
        try { return Directory.GetFiles(path); }
        catch { return Array.Empty<string>(); }
    }

    // ---------------------------------------------------------------- Log download

    public static Task DownloadLogsAsync(string pc, Action<string> log, CancellationToken ct) => Task.Run(() =>
    {
        var share = $@"\\{pc}\c$";
        if (!Directory.Exists(share))
            throw new IOException($"Cannot reach {share}.");

        var dest = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            "RemoteLogs",
            $"{pc}_{DateTime.Now:yyyyMMdd_HHmmss}");
        Directory.CreateDirectory(dest);
        log($"Saving to {dest}");
        log("");

        // 1) Event logs - exported on the remote PC via the EventLog RPC API, then pulled over c$.
        log("Exporting event logs...");
        using (var session = new EventLogSession(pc))
        {
            foreach (var logName in new[] { "System", "Application", "Setup" })
            {
                ct.ThrowIfCancellationRequested();
                var remoteFile = $@"C:\Windows\Temp\HDPT_{logName}.evtx";
                var remoteUnc = Path.Combine(share, "Windows", "Temp", $"HDPT_{logName}.evtx");
                try
                {
                    if (File.Exists(remoteUnc)) File.Delete(remoteUnc);
                    session.ExportLog(logName, PathType.LogName, "*", remoteFile);
                    File.Copy(remoteUnc, Path.Combine(dest, $"{logName}.evtx"), true);
                    File.Delete(remoteUnc);
                    log($"  {logName}.evtx");
                }
                catch (Exception ex)
                {
                    log($"  {logName}: FAILED - {ex.Message}");
                }
            }
        }

        // 2) Text logs straight from the admin share.
        log("");
        log("Copying log files...");
        CopyIfExists(Path.Combine(share, @"Windows\Logs\CBS\CBS.log"), dest, log);
        CopyIfExists(Path.Combine(share, @"Windows\Logs\DISM\dism.log"), dest, log);
        CopyIfExists(Path.Combine(share, @"Windows\Panther\setupact.log"), dest, log);
        CopyIfExists(Path.Combine(share, @"Windows\Panther\setuperr.log"), dest, log);

        // 3) Configuration Manager client logs, if the client is installed.
        var ccm = Path.Combine(share, @"Windows\CCM\Logs");
        if (Directory.Exists(ccm))
        {
            var ccmDest = Directory.CreateDirectory(Path.Combine(dest, "CCM")).FullName;
            var count = 0;
            foreach (var file in SafeFiles(ccm))
            {
                ct.ThrowIfCancellationRequested();
                try { CopyShared(file, Path.Combine(ccmDest, Path.GetFileName(file))); count++; }
                catch { /* skip locked */ }
            }
            log($"  CCM\\ ({count} files)");
        }

        log("");
        log("Done. Opening folder...");
        ProcessLauncher.Launch("explorer.exe", $"\"{dest}\"");
    }, ct);

    private static void CopyIfExists(string source, string destFolder, Action<string> log)
    {
        if (!File.Exists(source)) return;
        try
        {
            CopyShared(source, Path.Combine(destFolder, Path.GetFileName(source)));
            log($"  {Path.GetFileName(source)}");
        }
        catch (Exception ex)
        {
            log($"  {Path.GetFileName(source)}: FAILED - {ex.Message}");
        }
    }

    /// <summary>Copies a file that may be held open for writing (e.g. CBS.log).</summary>
    private static void CopyShared(string source, string dest)
    {
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var output = File.Create(dest);
        input.CopyTo(output);
    }

    // ---------------------------------------------------------------- Ping

    public static async Task PingAsync(string pc, Action<string> log, CancellationToken ct)
    {
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(pc, ct);
            log($"{pc} resolves to: {string.Join(", ", addresses.Select(a => a.ToString()))}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log($"DNS lookup failed: {ex.Message}");
        }

        log("Pinging continuously (like ping -t). Press Stop to end and show statistics.");
        log("");

        using var ping = new Ping();
        int sent = 0, received = 0;
        long min = long.MaxValue, max = 0, total = 0; // running stats, so memory stays flat however long it runs
        var lastReplyOk = false;

        // Stopping is the normal way to end a continuous ping, so cancellation
        // finishes with statistics instead of being reported as an error.
        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                sent++;
                lastReplyOk = false;
                try
                {
                    var reply = await ping.SendPingAsync(pc, TimeSpan.FromSeconds(2), cancellationToken: ct);
                    if (reply.Status == IPStatus.Success)
                    {
                        received++;
                        lastReplyOk = true;
                        min = Math.Min(min, reply.RoundtripTime);
                        max = Math.Max(max, reply.RoundtripTime);
                        total += reply.RoundtripTime;
                        log($"[{DateTime.Now:HH:mm:ss}] Reply from {reply.Address}: time={reply.RoundtripTime}ms{(reply.Options is { } o ? $" TTL={o.Ttl}" : "")}");
                    }
                    else
                    {
                        log($"[{DateTime.Now:HH:mm:ss}] Request failed: {reply.Status}");
                    }
                }
                catch (PingException ex)
                {
                    log($"[{DateTime.Now:HH:mm:ss}] Ping failed: {ex.InnerException?.Message ?? ex.Message}");
                }

                await Task.Delay(1000, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Stopped by the user - fall through to the summary.
        }

        log("");
        log($"Packets: Sent = {sent}, Received = {received}, Lost = {sent - received} ({(sent == 0 ? 0 : (sent - received) * 100 / sent)}% loss)");
        if (received > 0)
            log($"Round trip: Min = {min}ms, Max = {max}ms, Avg = {total / received}ms");
        log(lastReplyOk ? $"{pc} is ONLINE" : $"{pc} is OFFLINE / not responding (last ping)");
    }

    // ---------------------------------------------------------------- Reboot

    public static async Task RebootAsync(string pc, Action<string> log, CancellationToken ct)
    {
        log($"Sending forced restart to {pc} via WMI...");

        await Task.Run(() =>
        {
            var scope = WmiHelper.Connect(pc, enablePrivileges: true);
            using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM Win32_OperatingSystem"));
            foreach (ManagementObject os in searcher.Get())
            {
                using (os)
                {
                    // 2 = Reboot, 4 = Forced
                    var result = os.InvokeMethod("Win32Shutdown", new object[] { 6, 0 });
                    var code = Convert.ToInt32(result);
                    if (code != 0) throw new InvalidOperationException($"Win32Shutdown returned {code}.");
                }
            }
        }, ct);

        log("Restart command accepted.");
        log("");
        log("Waiting for the PC to go offline...");

        var sw = Stopwatch.StartNew();
        using var ping = new Ping();

        async Task<bool> IsUp()
        {
            try { return (await ping.SendPingAsync(pc, TimeSpan.FromSeconds(1), cancellationToken: ct)).Status == IPStatus.Success; }
            catch (PingException) { return false; }
        }

        while (await IsUp())
        {
            if (sw.Elapsed > TimeSpan.FromMinutes(3)) { log("PC never went offline (still pinging after 3 minutes)."); return; }
            await Task.Delay(2000, ct);
        }
        log($"Offline after {sw.Elapsed:m\\:ss}. Waiting for it to come back...");

        while (!await IsUp())
        {
            if (sw.Elapsed > TimeSpan.FromMinutes(15)) { log("PC has not come back after 15 minutes."); return; }
            await Task.Delay(3000, ct);
        }
        log($"{pc} is back ONLINE after {sw.Elapsed:m\\:ss}. (Services may still be starting.)");
    }

    // ---------------------------------------------------------------- Send message

    /// <summary>Sends a message box to every session on the PC using msg.exe.</summary>
    public static async Task<(bool ok, string output)> SendMessageAsync(string pc, string message, int seconds)
    {
        var msg = Path.Combine(Environment.SystemDirectory, "msg.exe");
        var psi = new ProcessStartInfo(msg)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("*");
        psi.ArgumentList.Add($"/server:{pc}");
        psi.ArgumentList.Add($"/time:{seconds}");
        psi.ArgumentList.Add(message);

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        var output = ((await stdout) + (await stderr)).Trim();
        return (process.ExitCode == 0, output);
    }

    // ---------------------------------------------------------------- helpers

    public static string FormatBytes(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.00} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.0} MB",
        >= 1L << 10 => $"{bytes / 1024.0:0} KB",
        _ => $"{bytes} B",
    };
}
