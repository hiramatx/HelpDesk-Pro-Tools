using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HelpDesk_Pro_Tools.Models;
using HelpDesk_Pro_Tools.Services;

namespace HelpDesk_Pro_Tools.ViewModels;

public partial class PcDetailsViewModel : ViewModelBase
{
    public PcDetailsViewModel(string pcName)
    {
        PcName = pcName;
    }

    public string PcName { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial bool HasData { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    // ---- Utilization gauges
    [ObservableProperty] public partial double CpuPercent { get; set; }
    [ObservableProperty] public partial double RamPercent { get; set; }
    [ObservableProperty] public partial string RamText { get; set; } = "";
    [ObservableProperty] public partial double DiskPercent { get; set; }
    [ObservableProperty] public partial string DiskText { get; set; } = "";

    // ---- Sections. PC / Software are 2 columns, Users / Hardware are 1 column.
    public ObservableCollection<InfoRow> PcRows { get; } = new();
    public ObservableCollection<InfoField> UserFields { get; } = new();
    public ObservableCollection<InfoField> HardwareFields { get; } = new();
    public ObservableCollection<InfoRow> SoftwareRows { get; } = new();

    public ObservableCollection<DeviceError> DeviceErrors { get; } = new();
    [ObservableProperty] public partial bool HasDeviceErrors { get; set; }

    [ObservableProperty] public partial string? Warnings { get; set; }

    private bool CanRefresh() => !IsLoading;

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    public async Task RefreshAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var d = await PcInfoService.GetDetailsAsync(PcName);

            CpuPercent = d.CpuPercent;
            RamPercent = d.RamPercent;
            RamText = $"{d.RamUsedGb:0.0} / {d.RamTotalGb:0.0} GB";
            DiskPercent = d.DiskPercent;
            DiskText = $"{d.DiskUsedGb:0} / {d.DiskTotalGb:0} GB  (C:)";

            Reset(PcRows, ToRows(BuildPc(d)));
            Reset(UserFields, BuildUsers(d));
            Reset(HardwareFields, BuildHardware(d));
            Reset(SoftwareRows, ToRows(BuildSoftware(d)));
            Reset(DeviceErrors, d.DeviceErrors);
            HasDeviceErrors = DeviceErrors.Count > 0;

            Warnings = d.Warnings.Count > 0 ? "Some details could not be read:\n• " + string.Join("\n• ", d.Warnings) : null;
            HasData = true;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Could not connect to {PcName}: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    // 2-column sections list their fields left-to-right, row by row (2 per row).

    private static IEnumerable<InfoField> BuildPc(PcDetails d)
    {
        yield return new("PC Name", d.ComputerName);
        yield return new("PC Model", d.Model);
        yield return new("OS", d.OperatingSystem);
        yield return new("Serial No.", d.SerialNumber);
        yield return new("UAC/LUA", d.Uac);
        yield return new("DC", d.DomainController);
        yield return BuildIpAddress(d.IpAddress);
        yield return new("Network Speed", d.NetworkSpeed);
        yield return new("Last Boot Time", d.LastBoot?.ToString("g") ?? "");
        yield return BuildUptime(d.LastBoot);
        yield return new("PC OU", d.PcOu);
        yield return InfoField.Spacer;
    }

    // Uptime: green up to 48 hours, yellow over 48 hours, red over 96 hours.
    private const double UptimeCautionHours = 48;
    private const double UptimeBadHours = 96;

    private static InfoField BuildUptime(DateTime? lastBoot)
    {
        if (lastBoot is not { } boot) return new InfoField("Uptime", "");

        var uptime = DateTime.Now - boot;
        var (status, tip) = uptime.TotalHours switch
        {
            > UptimeBadHours => (FieldStatus.Bad, $"Up more than {UptimeBadHours:0} hours - a restart is overdue"),
            > UptimeCautionHours => (FieldStatus.Caution, $"Up more than {UptimeCautionHours:0} hours"),
            _ => (FieldStatus.Ok, $"Restarted within the last {UptimeCautionHours:0} hours"),
        };
        return new InfoField("Uptime", FormatUptime(uptime)) { Status = status, ToolTip = tip };
    }

    // IP: orange on the 105.195.x.x range, green otherwise.
    private const string OrangeIpPrefix = "105.195.";

    private static InfoField BuildIpAddress(string ipText)
    {
        var ips = ipText.Split(", ", StringSplitOptions.RemoveEmptyEntries)
            .Where(ip => System.Net.IPAddress.TryParse(ip, out _))
            .ToList();
        if (ips.Count == 0) return new InfoField("IP Address", ipText);

        var status = ips.Any(ip => ip.StartsWith(OrangeIpPrefix, StringComparison.Ordinal)) ? FieldStatus.Warn : FieldStatus.Ok;
        return new InfoField("IP Address", ipText) { Status = status };
    }

    private static IEnumerable<InfoField> BuildUsers(PcDetails d)
    {
        yield return new("Logged in User", d.LoggedInUser);
        yield return new("Logged in User OU", d.LoggedInUserOu);
        yield return new("Logged in From", d.LoggedInFrom);

        // Members are highlighted: local admins red, remote access groups orange.
        // "(none)", "Group not found" and read errors stay in normal text.
        yield return Group("Local Admin Members", d.LocalAdmins, FieldStatus.Bad);
        yield return Group("Remote Desktop Users", d.RemoteDesktopUsers, FieldStatus.Warn);
        yield return Group("Direct Access Users", d.DirectAccessUsers, FieldStatus.Warn);
    }

    private static InfoField Group(string label, GroupMembers group, FieldStatus statusWhenPopulated) =>
        new(label, group.Text) { Status = group.HasMembers ? statusWhenPopulated : FieldStatus.Normal };

    // One field per line.
    private static IEnumerable<InfoField> BuildHardware(PcDetails d)
    {
        var fields = new List<InfoField>
        {
            new("Processor", d.Processor),
            new("Total RAM", d.TotalRam),
            new("RAM Sticks", d.RamConfig),
            new("MAC Address", d.MacAddress),
        };

        AddNumbered(fields, "Video Card", d.VideoCards);
        AddNumbered(fields, "Monitor", d.Monitors.Select(m => $"{m.Name}, {m.Resolution}").ToList());
        fields.AddRange(d.Drives.Select(x => new InfoField(
            $"Drive {x.Letter.TrimEnd(':')}",
            $"Total {x.TotalGb:0} GB, Used {x.UsedGb:0} GB, Free {x.FreeGb:0} GB")));

        return fields;
    }

    /// <summary>One field per entry in Config\software.json, coloured by baselines.json.</summary>
    private static IEnumerable<InfoField> BuildSoftware(PcDetails d)
    {
        if (d.Software.Count == 0)
            yield return new InfoField("Software", "None configured - add programs to Config\\software.json");

        foreach (var s in d.Software)
            yield return new InfoField(s.Label, s.Value) { Status = s.Status, ToolTip = s.ToolTip };

        yield return BuildGpo("GPO System Date", d.GpoSystem);
        yield return BuildGpo("GPO User Date", d.GpoUser);
    }

    // GPO dates: green if Group Policy applied within the last 7 days (today included), red otherwise.
    private const int GpoFreshDays = 7;

    private static InfoField BuildGpo(string label, GpoDate gpo)
    {
        if (gpo.When is not { } when)
        {
            // No user logged in isn't a problem, so it stays in normal text.
            var status = gpo.Problem == "No user logged in" ? FieldStatus.Normal : FieldStatus.Bad;
            return new InfoField(label, gpo.Problem ?? "Unknown") { Status = status };
        }

        var fresh = when.Date >= DateTime.Today.AddDays(-(GpoFreshDays - 1));
        return new InfoField(label, when.ToString("g"))
        {
            Status = fresh ? FieldStatus.Ok : FieldStatus.Bad,
            ToolTip = fresh
                ? $"Group Policy applied within the last {GpoFreshDays} days"
                : $"Group Policy has not applied in the last {GpoFreshDays} days - try gpupdate /force",
        };
    }

    /// <summary>"Video Card 1", "Video Card 2"...</summary>
    private static void AddNumbered(List<InfoField> fields, string label, IReadOnlyList<string> values)
    {
        if (values.Count == 0)
            fields.Add(new InfoField($"{label} 1", "None found"));
        else
            fields.AddRange(values.Select((v, i) => new InfoField($"{label} {i + 1}", v)));
    }

    /// <summary>Pairs fields left/right into rows (a trailing odd field gets an empty right cell).</summary>
    private static IEnumerable<InfoRow> ToRows(IEnumerable<InfoField> fields) =>
        fields.Chunk(2).Select(pair => new InfoRow(pair[0], pair.Length > 1 ? pair[1] : InfoField.Spacer));

    private static void Reset<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items) target.Add(item);
    }

    private static string FormatUptime(TimeSpan t) =>
        $"{(int)t.TotalDays}d {t.Hours}h {t.Minutes}m {t.Seconds}s";
}
