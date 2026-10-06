using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Threading.Tasks;
using HelpDesk_Pro_Tools.Models;

namespace HelpDesk_Pro_Tools.Services;

/// <summary>Collects hardware / user / software details from a remote PC via WMI, the admin share and Active Directory.</summary>
public static class PcInfoService
{
    /// <summary>Name of the DirectAccess group: looked up as a local group on the PC first, then in AD.</summary>
    public const string DirectAccessGroupName = "Direct Access Users";

    private const string CurrentVersionKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";

    public static Task<PcDetails> GetDetailsAsync(string pc) => Task.Run(() =>
    {
        var d = new PcDetails { ComputerName = pc };

        try { d.PcOu = ActiveDirectoryService.GetComputerOu(pc); }
        catch (Exception ex) { d.PcOu = $"AD lookup failed: {ex.Message}"; }

        // A failed connection here means nothing else will work, so let it throw.
        var cimv2 = WmiHelper.Connect(pc);
        RemoteRegistry? registry = null;
        Try(d, "Registry", () => registry = new RemoteRegistry(pc));

        try
        {
            Try(d, "System", () => ReadSystem(cimv2, registry, d));
            Try(d, "CPU", () => ReadCpu(cimv2, d));
            Try(d, "Memory", () => ReadMemory(cimv2, d));
            Try(d, "Drives", () => ReadDrives(cimv2, d));
            Try(d, "Network", () => ReadNetwork(cimv2, d));
            Try(d, "Domain controller", () => ReadDomainController(cimv2, registry, d));
            Try(d, "Logged in user", () => ReadLoggedInUser(cimv2, d));
            Try(d, "Local groups", () => ReadGroups(pc, d));
            Try(d, "Video card", () => ReadVideo(cimv2, d));
            Try(d, "Monitors", () => ReadMonitors(pc, d));
            Try(d, "Software", () => ReadSoftware(pc, registry, d));
            Try(d, "Group Policy", () => ReadGpo(cimv2, registry, d));
            Try(d, "Device Manager", () => ReadDeviceErrors(cimv2, d));
        }
        finally
        {
            registry?.Dispose();
        }

        return d;
    });

    // ------------------------------------------------------------------ PC

    private static void ReadSystem(ManagementScope cimv2, RemoteRegistry? registry, PcDetails d)
    {
        var cs = WmiHelper.Query(cimv2, "SELECT Name, Manufacturer, Model FROM Win32_ComputerSystem").First();
        d.ComputerName = cs["Name"]?.ToString() ?? d.ComputerName;
        d.Model = $"{cs["Manufacturer"]} {cs["Model"]}".Trim();

        var bios = WmiHelper.Query(cimv2, "SELECT SerialNumber FROM Win32_BIOS").FirstOrDefault();
        d.SerialNumber = bios?["SerialNumber"]?.ToString()?.Trim() ?? "";

        var os = WmiHelper.Query(cimv2, "SELECT Caption, BuildNumber, LastBootUpTime FROM Win32_OperatingSystem").First();
        if (os["LastBootUpTime"] is string boot) d.LastBoot = ManagementDateTimeConverter.ToDateTime(boot);

        // "Windows 11 Pro 25H2 (26200.6584)"
        var caption = os["Caption"]?.ToString()?.Replace("Microsoft ", "").Trim() ?? "";
        var displayVersion = registry?.GetString(CurrentVersionKey, "DisplayVersion")
                             ?? registry?.GetString(CurrentVersionKey, "ReleaseId");
        var build = registry?.GetString(CurrentVersionKey, "CurrentBuild") ?? os["BuildNumber"]?.ToString();
        var ubr = registry?.GetDword(CurrentVersionKey, "UBR");
        var buildText = build is null ? "" : ubr is null ? $" ({build})" : $" ({build}.{ubr})";
        d.OperatingSystem = $"{caption} {displayVersion}".Trim() + buildText;

        // UAC = EnableLUA (missing value means the Windows default: enabled)
        var lua = registry?.GetDword(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "EnableLUA");
        d.Uac = registry is null ? "Unknown" : lua == 0 ? "Disabled" : "Enabled";
    }

    private static void ReadDomainController(ManagementScope cimv2, RemoteRegistry? registry, PcDetails d)
    {
        var dc = WmiHelper.Query(cimv2, "SELECT DomainControllerName FROM Win32_NTDomain WHERE DomainControllerName IS NOT NULL")
            .Select(x => x["DomainControllerName"]?.ToString())
            .FirstOrDefault(x => !string.IsNullOrEmpty(x));

        // Fallback: the DC that last applied Group Policy.
        dc ??= registry?.GetString(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Group Policy\History", "DCName");

        d.DomainController = string.IsNullOrEmpty(dc) ? "Not found" : dc.TrimStart('\\');
    }

    private static void ReadNetwork(ManagementScope cimv2, PcDetails d)
    {
        var adapters = WmiHelper.Query(cimv2,
            "SELECT Index, IPAddress, DefaultIPGateway, MACAddress FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled = TRUE");

        // Prefer the adapter that has a default gateway (the one actually in use).
        var nic = adapters.FirstOrDefault(a => a["DefaultIPGateway"] is string[] { Length: > 0 }) ?? adapters.FirstOrDefault();
        if (nic is null)
        {
            d.IpAddress = d.MacAddress = d.NetworkSpeed = "No active adapter";
            return;
        }

        var ips = (nic["IPAddress"] as string[] ?? Array.Empty<string>()).Where(ip => ip.Contains('.')).ToList();
        d.IpAddress = ips.Count > 0 ? string.Join(", ", ips) : "None";
        d.MacAddress = nic["MACAddress"]?.ToString() ?? "";

        var adapter = WmiHelper.Query(cimv2, $"SELECT Name, Speed FROM Win32_NetworkAdapter WHERE Index = {nic["Index"]}").FirstOrDefault();
        d.NetworkSpeed = adapter?["Speed"] is { } speed
            ? $"{Convert.ToDouble(speed) / 1_000_000_000:0.##} Gbits/sec"
            : "Unknown";
    }

    // ------------------------------------------------------------------ Utilization / hardware

    private static void ReadCpu(ManagementScope cimv2, PcDetails d)
    {
        var cpus = WmiHelper.Query(cimv2, "SELECT Name, LoadPercentage FROM Win32_Processor");
        d.CpuPercent = cpus.Count > 0 ? cpus.Average(p => Convert.ToDouble(p["LoadPercentage"] ?? 0)) : 0;
        d.Processor = string.Join(", ", cpus.Select(p => p["Name"]?.ToString()?.Trim()).Distinct());
    }

    private static void ReadMemory(ManagementScope cimv2, PcDetails d)
    {
        var os = WmiHelper.Query(cimv2, "SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem").First();
        var totalKb = Convert.ToDouble(os["TotalVisibleMemorySize"]);
        var freeKb = Convert.ToDouble(os["FreePhysicalMemory"]);
        d.RamTotalGb = totalKb / 1024 / 1024;
        d.RamUsedGb = (totalKb - freeKb) / 1024 / 1024;
        d.RamPercent = totalKb > 0 ? (totalKb - freeKb) / totalKb * 100 : 0;

        // Installed sticks: "DIMM A1: 8192 MB, DIMM B1: 8192 MB (2 of 4 slots)"
        var sticks = WmiHelper.Query(cimv2, "SELECT DeviceLocator, BankLabel, Capacity FROM Win32_PhysicalMemory")
            .Select(s => (
                Slot: (s["DeviceLocator"]?.ToString() ?? s["BankLabel"]?.ToString() ?? "Slot").Trim(),
                Mb: Convert.ToDouble(s["Capacity"] ?? 0) / 1024 / 1024))
            .ToList();

        var installedMb = sticks.Sum(s => s.Mb);
        d.TotalRam = $"{(installedMb > 0 ? installedMb : totalKb / 1024):N0} MB";

        var slots = WmiHelper.Query(cimv2, "SELECT MemoryDevices FROM Win32_PhysicalMemoryArray")
            .Sum(a => Convert.ToInt32(a["MemoryDevices"] ?? 0));
        var list = string.Join(", ", sticks.Select(s => $"{s.Slot}: {s.Mb:N0} MB"));
        d.RamConfig = slots > 0 ? $"{list} ({sticks.Count} of {slots} slots)" : list;
    }

    private static void ReadDrives(ManagementScope cimv2, PcDetails d)
    {
        foreach (var disk in WmiHelper.Query(cimv2, "SELECT DeviceID, Size, FreeSpace FROM Win32_LogicalDisk WHERE DriveType = 3"))
        {
            var size = Convert.ToDouble(disk["Size"] ?? 0) / 1024 / 1024 / 1024;
            var free = Convert.ToDouble(disk["FreeSpace"] ?? 0) / 1024 / 1024 / 1024;
            d.Drives.Add(new DriveSpace(disk["DeviceID"]?.ToString() ?? "?", size, size - free, free));
        }

        var c = d.Drives.FirstOrDefault(x => x.Letter.Equals("C:", StringComparison.OrdinalIgnoreCase));
        if (c is not null)
        {
            d.DiskTotalGb = c.TotalGb;
            d.DiskUsedGb = c.UsedGb;
            d.DiskPercent = c.TotalGb > 0 ? c.UsedGb / c.TotalGb * 100 : 0;
        }
    }

    public const string ExcludedVideoCardsFile = "excluded_video_cards.json";

    private static void ReadVideo(ManagementScope cimv2, PcDetails d)
    {
        // Cards listed in Config\excluded_video_cards.json (e.g. virtual display adapters) are not shown.
        var excluded = Wildcard.Compile(null);
        try
        {
            excluded = Wildcard.Compile(ConfigFiles.Load<ExcludedVideoCards>(ExcludedVideoCardsFile)?.Excluded);
        }
        catch (Exception ex)
        {
            d.Warnings.Add($"{ex.Message} (no video cards were excluded)");
        }

        foreach (var vc in WmiHelper.Query(cimv2, "SELECT Name FROM Win32_VideoController"))
        {
            var name = vc["Name"]?.ToString()?.Trim() ?? "Unknown";
            if (!Wildcard.IsMatch(name, excluded))
                d.VideoCards.Add(name);
        }
    }

    private static void ReadMonitors(string pc, PcDetails d)
    {
        var wmi = WmiHelper.Connect(pc, @"root\wmi");

        // Native (preferred) resolution per monitor, keyed by instance name.
        var resolutions = WmiHelper.Query(wmi, "SELECT InstanceName, PreferredMonitorSourceModeIndex, MonitorSourceModes FROM WmiMonitorListedSupportedSourceModes")
            .ToDictionary(
                m => m["InstanceName"]?.ToString() ?? "",
                m =>
                {
                    if (m["MonitorSourceModes"] is not ManagementBaseObject[] modes || modes.Length == 0) return "Unknown resolution";
                    var index = Convert.ToInt32(m["PreferredMonitorSourceModeIndex"]);
                    var mode = modes[Math.Clamp(index, 0, modes.Length - 1)];
                    return $"{mode["HorizontalActivePixels"]} x {mode["VerticalActivePixels"]}";
                },
                StringComparer.OrdinalIgnoreCase);

        foreach (var mon in WmiHelper.Query(wmi, "SELECT InstanceName, UserFriendlyName, ManufacturerName, Active FROM WmiMonitorID"))
        {
            if (mon["Active"] is false) continue;
            var name = DecodeWmiString(mon["UserFriendlyName"]);
            if (string.IsNullOrWhiteSpace(name))
                name = $"{DecodeWmiString(mon["ManufacturerName"])} display".Trim();
            var instance = mon["InstanceName"]?.ToString() ?? "";
            d.Monitors.Add(new MonitorInfo(name, resolutions.GetValueOrDefault(instance, "Unknown resolution")));
        }
    }

    // WmiMonitorID strings are uint16[] character arrays padded with zeros.
    private static string DecodeWmiString(object? value) => value is ushort[] chars
        ? new string(chars.TakeWhile(c => c != 0).Select(c => (char)c).ToArray())
        : "";

    private static void ReadDeviceErrors(ManagementScope cimv2, PcDetails d)
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
    }

    // ------------------------------------------------------------------ Users

    private static void ReadLoggedInUser(ManagementScope cimv2, PcDetails d)
    {
        // Win32_ComputerSystem.UserName is the console user; it's empty for RDP-only sessions,
        // in which case the owner of explorer.exe is the remote user.
        var user = WmiHelper.Query(cimv2, "SELECT UserName FROM Win32_ComputerSystem").First()["UserName"]?.ToString();
        var from = "Console";

        if (string.IsNullOrEmpty(user))
        {
            foreach (var proc in WmiHelper.Query(cimv2, "SELECT Handle FROM Win32_Process WHERE Name = 'explorer.exe'").Cast<ManagementObject>())
            {
                var args = new object[2];
                if (Convert.ToInt32(proc.InvokeMethod("GetOwner", args)) == 0 && args[0] is string owner)
                {
                    user = owner;
                    from = "Remote (RDP)";
                    break;
                }
            }
        }

        if (string.IsNullOrEmpty(user))
        {
            d.LoggedInUser = "No user logged in";
            d.LoggedInFrom = "-";
            return;
        }

        // Omit the domain: "CORP\jdoe" -> "jdoe"
        var sam = user.Contains('\\') ? user[(user.IndexOf('\\') + 1)..] : user;
        d.LoggedInUser = sam;
        d.LoggedInFrom = from;
        try { d.LoggedInUserOu = ActiveDirectoryService.GetUserOu(sam); }
        catch (Exception ex) { d.LoggedInUserOu = $"AD lookup failed: {ex.Message}"; }
    }

    // ------------------------------------------------------------------ Group Policy

    private const string GpoStateKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Group Policy\State";
    private const string GpoCoreExtension = @"Extension-List\{00000000-0000-0000-0000-000000000000}";

    /// <summary>Last Group Policy refresh for the computer and the logged-in user, from HKLM\...\Group Policy\State.</summary>
    private static void ReadGpo(ManagementScope cimv2, RemoteRegistry? registry, PcDetails d)
    {
        if (registry is null)
        {
            d.GpoSystem = d.GpoUser = GpoDate.Unknown("Remote registry not available");
            return;
        }

        d.GpoSystem = ReadGpoTime(registry, "Machine");

        if (d.LoggedInUser is "" or "No user logged in")
        {
            d.GpoUser = GpoDate.Unknown("No user logged in");
            return;
        }

        // Group Policy keeps user state under the user's SID; explorer.exe's owner gives us that SID.
        var sid = FindUserSid(cimv2, d.LoggedInUser);
        d.GpoUser = sid is null
            ? GpoDate.Unknown($"Could not find the SID of {d.LoggedInUser}")
            : ReadGpoTime(registry, sid);
    }

    private static GpoDate ReadGpoTime(RemoteRegistry registry, string subKey)
    {
        var key = $@"{GpoStateKey}\{subKey}\{GpoCoreExtension}";
        var when = FileTime(registry, key, "EndTime") ?? FileTime(registry, key, "StartTime");
        return when is null ? GpoDate.Unknown("Group Policy has not been applied") : new GpoDate(when, null);
    }

    /// <summary>A FILETIME stored as two DWORDs ("EndTimeHi" / "EndTimeLo"), in local time.</summary>
    private static DateTime? FileTime(RemoteRegistry registry, string key, string name)
    {
        var hi = registry.GetDword(key, name + "Hi");
        var lo = registry.GetDword(key, name + "Lo");
        if (hi is null || lo is null) return null;
        var ft = ((long)hi.Value << 32) | lo.Value;
        return ft > 0 ? DateTime.FromFileTimeUtc(ft).ToLocalTime() : null;
    }

    private static string? FindUserSid(ManagementScope cimv2, string sam)
    {
        foreach (var proc in WmiHelper.Query(cimv2, "SELECT Handle FROM Win32_Process WHERE Name = 'explorer.exe'").Cast<ManagementObject>())
        {
            var owner = new object[2];
            if (Convert.ToInt32(proc.InvokeMethod("GetOwner", owner)) != 0 ||
                !string.Equals(owner[0] as string, sam, StringComparison.OrdinalIgnoreCase))
                continue;

            var sid = new object[1];
            if (Convert.ToInt32(proc.InvokeMethod("GetOwnerSid", sid)) == 0 && sid[0] is string s)
                return s;
        }
        return null;
    }

    private static void ReadGroups(string pc, PcDetails d)
    {
        UserExclusions exclusions;
        try
        {
            exclusions = UserExclusions.Load();
        }
        catch (Exception ex)
        {
            d.Warnings.Add($"{ex.Message} (nothing was excluded)");
            exclusions = UserExclusions.None;
        }

        d.LocalAdmins = SafeGroup(() =>
            Join(LocalGroupService.GetMembers(pc, "Administrators", d.ComputerName), exclusions.FilterLocalAdmins));
        d.RemoteDesktopUsers = SafeGroup(() =>
            Join(LocalGroupService.GetMembers(pc, "Remote Desktop Users", d.ComputerName), exclusions.FilterRemoteDesktopUsers));
        d.DirectAccessUsers = SafeGroup(() =>
            Join(LocalGroupService.GetMembers(pc, DirectAccessGroupName, d.ComputerName)
                 ?? ActiveDirectoryService.GetGroupMembers(DirectAccessGroupName), exclusions.FilterDirectAccessUsers));
    }

    /// <summary>
    /// Applies exclusions (matched against the full name, domain included), then shows the
    /// remaining members without their domain as a comma-separated list.
    /// </summary>
    private static GroupMembers Join(List<string>? members, Func<IEnumerable<string>, IEnumerable<string>> filter)
    {
        if (members is null) return new GroupMembers("Group not found", false);

        var shown = filter(members)
            .Select(WithoutDomain)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return shown.Count == 0
            ? new GroupMembers("(none)", false)
            : new GroupMembers(string.Join(", ", shown), true);
    }

    // "CORP\Domain Admins" -> "Domain Admins"
    private static string WithoutDomain(string name) =>
        name.Contains('\\') ? name[(name.LastIndexOf('\\') + 1)..] : name;

    private static GroupMembers SafeGroup(Func<GroupMembers> read)
    {
        try { return read(); }
        catch (Exception ex) { return new GroupMembers($"Could not read: {ex.Message}", false); }
    }

    // ------------------------------------------------------------------ Software

    private static void ReadSoftware(string pc, RemoteRegistry? registry, PcDetails d)
    {
        try
        {
            d.Software.AddRange(SoftwareInventoryService.Read(pc, registry));
        }
        catch (Exception ex)
        {
            // Missing or invalid software.json: show the problem in the Software card itself.
            d.Software.Add(new SoftwareResult(SoftwareInventoryService.CatalogFile, "Could not load", FieldStatus.Warn, ex.Message));
        }
    }

    // ------------------------------------------------------------------ helpers

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
