using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Threading.Tasks;
using HelpDesk_Pro_Tools.Models;

namespace HelpDesk_Pro_Tools.Services;

/// <summary>Collects hardware / user details from a remote PC via WMI and Active Directory.</summary>
public static class PcInfoService
{
    public static Task<PcDetails> GetDetailsAsync(string pc) => Task.Run(() =>
    {
        var d = new PcDetails { PcName = pc };

        try { d.PcOu = ActiveDirectoryService.GetComputerOu(pc); }
        catch (Exception ex) { d.PcOu = $"AD lookup failed: {ex.Message}"; }

        // A failed connection here means nothing else will work, so let it throw.
        var cimv2 = WmiHelper.Connect(pc);

        Try(d, "Operating system / RAM", () =>
        {
            var os = WmiHelper.Query(cimv2, "SELECT Caption, TotalVisibleMemorySize, FreePhysicalMemory, LastBootUpTime FROM Win32_OperatingSystem").First();
            d.OperatingSystem = os["Caption"]?.ToString()?.Replace("Microsoft ", "") ?? "";
            if (os["LastBootUpTime"] is string boot) d.LastBoot = ManagementDateTimeConverter.ToDateTime(boot);

            var totalKb = Convert.ToDouble(os["TotalVisibleMemorySize"]);
            var freeKb = Convert.ToDouble(os["FreePhysicalMemory"]);
            d.RamTotalGb = totalKb / 1024 / 1024;
            d.RamUsedGb = (totalKb - freeKb) / 1024 / 1024;
            d.RamPercent = totalKb > 0 ? (totalKb - freeKb) / totalKb * 100 : 0;
        });

        Try(d, "CPU", () =>
        {
            var loads = WmiHelper.Query(cimv2, "SELECT LoadPercentage FROM Win32_Processor")
                .Select(p => Convert.ToDouble(p["LoadPercentage"] ?? 0))
                .ToList();
            d.CpuPercent = loads.Count > 0 ? loads.Average() : 0;
        });

        Try(d, "Storage", () =>
        {
            var disk = WmiHelper.Query(cimv2, "SELECT Size, FreeSpace FROM Win32_LogicalDisk WHERE DeviceID = 'C:'").FirstOrDefault();
            if (disk is null) return;
            var size = Convert.ToDouble(disk["Size"]);
            var free = Convert.ToDouble(disk["FreeSpace"]);
            d.DiskTotalGb = size / 1024 / 1024 / 1024;
            d.DiskUsedGb = (size - free) / 1024 / 1024 / 1024;
            d.DiskPercent = size > 0 ? (size - free) / size * 100 : 0;
        });

        Try(d, "Logged in user", () =>
        {
            var user = WmiHelper.Query(cimv2, "SELECT UserName FROM Win32_ComputerSystem").First()["UserName"]?.ToString();

            // Win32_ComputerSystem.UserName is empty for RDP sessions; fall back to explorer.exe's owner.
            if (string.IsNullOrEmpty(user))
            {
                foreach (ManagementObject proc in WmiHelper.Query(cimv2, "SELECT Handle FROM Win32_Process WHERE Name = 'explorer.exe'").Cast<ManagementObject>())
                {
                    var args = new object[2];
                    if (Convert.ToInt32(proc.InvokeMethod("GetOwner", args)) == 0 && args[0] is string owner)
                    {
                        user = $"{args[1]}\\{owner}";
                        break;
                    }
                }
            }

            if (string.IsNullOrEmpty(user))
            {
                d.LoggedInUser = "No user logged in";
                return;
            }

            d.LoggedInUser = user;
            var sam = user.Contains('\\') ? user[(user.IndexOf('\\') + 1)..] : user;
            try { d.LoggedInUserOu = ActiveDirectoryService.GetUserOu(sam); }
            catch (Exception ex) { d.LoggedInUserOu = $"AD lookup failed: {ex.Message}"; }
        });

        Try(d, "Video card", () =>
        {
            foreach (var vc in WmiHelper.Query(cimv2,
                         "SELECT Name, DriverVersion, CurrentHorizontalResolution, CurrentVerticalResolution, CurrentRefreshRate FROM Win32_VideoController"))
            {
                var res = vc["CurrentHorizontalResolution"] is null
                    ? "Not driving a display"
                    : $"{vc["CurrentHorizontalResolution"]} x {vc["CurrentVerticalResolution"]} @ {vc["CurrentRefreshRate"]} Hz";
                d.VideoCards.Add(new VideoCardInfo(vc["Name"]?.ToString() ?? "Unknown", vc["DriverVersion"]?.ToString() ?? "", res));
            }
        });

        Try(d, "Monitors", () => ReadMonitors(pc, d));

        Try(d, "Device Manager", () =>
        {
            foreach (var dev in WmiHelper.Query(cimv2, "SELECT Name, DeviceID, ConfigManagerErrorCode FROM Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0"))
            {
                var code = Convert.ToInt32(dev["ConfigManagerErrorCode"]);
                if (code == 45) continue; // "not currently connected" - hidden in Device Manager by default
                d.DeviceErrors.Add(new DeviceError(
                    dev["Name"]?.ToString() ?? dev["DeviceID"]?.ToString() ?? "Unknown device",
                    code,
                    DescribeError(code)));
            }
        });

        return d;
    });

    private static void ReadMonitors(string pc, PcDetails d)
    {
        var wmi = WmiHelper.Connect(pc, @"root\wmi");

        // Native (preferred) resolution per monitor, keyed by instance name.
        var resolutions = WmiHelper.Query(wmi, "SELECT InstanceName, PreferredMonitorSourceModeIndex, MonitorSourceModes FROM WmiMonitorListedSupportedSourceModes")
            .ToDictionary(
                m => m["InstanceName"]?.ToString() ?? "",
                m =>
                {
                    if (m["MonitorSourceModes"] is not ManagementBaseObject[] modes || modes.Length == 0) return "Unknown";
                    var index = Convert.ToInt32(m["PreferredMonitorSourceModeIndex"]);
                    var mode = modes[Math.Clamp(index, 0, modes.Length - 1)];
                    return $"{mode["HorizontalActivePixels"]} x {mode["VerticalActivePixels"]} (native)";
                },
                StringComparer.OrdinalIgnoreCase);

        foreach (var mon in WmiHelper.Query(wmi, "SELECT InstanceName, UserFriendlyName, ManufacturerName, Active FROM WmiMonitorID"))
        {
            if (mon["Active"] is false) continue;
            var name = DecodeWmiString(mon["UserFriendlyName"]);
            if (string.IsNullOrWhiteSpace(name))
                name = $"{DecodeWmiString(mon["ManufacturerName"])} display".Trim();
            var instance = mon["InstanceName"]?.ToString() ?? "";
            d.Monitors.Add(new MonitorInfo(name, resolutions.GetValueOrDefault(instance, "Unknown")));
        }
    }

    // WmiMonitorID strings are uint16[] character arrays padded with zeros.
    private static string DecodeWmiString(object? value) => value is ushort[] chars
        ? new string(chars.TakeWhile(c => c != 0).Select(c => (char)c).ToArray())
        : "";

    private static void Try(PcDetails d, string section, Action action)
    {
        try { action(); }
        catch (Exception ex) { d.Warnings.Add($"{section}: {ex.Message}"); }
    }

    private static string DescribeError(int code) => code switch
    {
        1 => "Device is not configured correctly (Code 1)",
        3 => "Driver may be corrupted or system is low on memory (Code 3)",
        10 => "Device cannot start (Code 10)",
        12 => "Not enough free resources (Code 12)",
        14 => "Restart required (Code 14)",
        18 => "Drivers need to be reinstalled (Code 18)",
        19 => "Registry configuration is incomplete or damaged (Code 19)",
        21 => "Windows is removing this device (Code 21)",
        22 => "Device is disabled (Code 22)",
        24 => "Device is not present or not working properly (Code 24)",
        28 => "Drivers are not installed (Code 28)",
        29 => "Device disabled by firmware (Code 29)",
        31 => "Device is not working properly (Code 31)",
        32 => "Driver service is disabled (Code 32)",
        37 => "Driver failed to initialize (Code 37)",
        39 => "Driver is corrupted or missing (Code 39)",
        43 => "Device reported a problem and was stopped (Code 43)",
        48 => "Driver blocked due to known issues (Code 48)",
        52 => "Driver signature could not be verified (Code 52)",
        _ => $"Device Manager error code {code}",
    };
}
