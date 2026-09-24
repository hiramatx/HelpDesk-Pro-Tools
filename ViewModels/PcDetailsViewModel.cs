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

    // ---- Sections. PC / Hardware / Software are 2 columns, Users is 1 column.
    public ObservableCollection<InfoRow> PcRows { get; } = new();
    public ObservableCollection<InfoField> UserFields { get; } = new();
    public ObservableCollection<InfoRow> HardwareRows { get; } = new();
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
            Reset(HardwareRows, ToRows(BuildHardware(d)));
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

    // Fields are listed left-to-right, row by row (2 per row).

    private static IEnumerable<InfoField> BuildPc(PcDetails d)
    {
        yield return new("PC Name", d.ComputerName);
        yield return new("PC Model", d.Model);
        yield return new("OS", d.OperatingSystem);
        yield return new("Serial No.", d.SerialNumber);
        yield return new("UAC/LUA", d.Uac);
        yield return new("DC", d.DomainController);
        yield return new("IP Address", d.IpAddress);
        yield return new("Network Speed", d.NetworkSpeed);
        yield return new("Last Boot Time", d.LastBoot?.ToString("g") ?? "");
        yield return new("Uptime", d.LastBoot is { } boot ? FormatUptime(DateTime.Now - boot) : "");
        yield return new("PC OU", d.PcOu);
        yield return InfoField.Spacer;
    }

    private static IEnumerable<InfoField> BuildUsers(PcDetails d)
    {
        yield return new("Logged in User", d.LoggedInUser);
        yield return new("Logged in User OU", d.LoggedInUserOu);
        yield return new("Logged in From", d.LoggedInFrom);
        yield return new("Local Admin Members", d.LocalAdmins);
        yield return new("Remote Desktop Users", d.RemoteDesktopUsers);
        yield return new("Direct Access Users", d.DirectAccessUsers);
    }

    private static IEnumerable<InfoField> BuildHardware(PcDetails d)
    {
        var fields = new List<InfoField>
        {
            new("Processor", d.Processor),
            new("Total RAM", d.TotalRam),
            new("MAC Address", d.MacAddress),
            new("RAM Sticks", d.RamConfig),
        };

        AddNumbered(fields, "Video Card", d.VideoCards);
        AddNumbered(fields, "Monitor", d.Monitors.Select(m => $"{m.Name}, {m.Resolution}").ToList());
        AddPairs(fields, d.Drives.Select(x => new InfoField(
            $"Drive {x.Letter.TrimEnd(':')}",
            $"Total {x.TotalGb:0} GB, Used {x.UsedGb:0} GB, Free {x.FreeGb:0} GB")).ToList());

        return fields;
    }

    private static IEnumerable<InfoField> BuildSoftware(PcDetails d)
    {
        yield return new("MS Edge Version", d.EdgeVersion);
        yield return new("Chrome Version", d.ChromeVersion);
        yield return new("MS Office Version", d.OfficeVersion);
        yield return InfoField.Spacer;
    }

    /// <summary>"Video Card 1", "Video Card 2"... always filling whole rows.</summary>
    private static void AddNumbered(List<InfoField> fields, string label, IReadOnlyList<string> values)
    {
        if (values.Count == 0)
        {
            AddPairs(fields, new List<InfoField> { new($"{label} 1", "None found") });
            return;
        }
        AddPairs(fields, values.Select((v, i) => new InfoField($"{label} {i + 1}", v)).ToList());
    }

    /// <summary>Adds items and pads with a spacer so the next group starts on a new row.</summary>
    private static void AddPairs(List<InfoField> fields, List<InfoField> items)
    {
        fields.AddRange(items);
        if (items.Count % 2 == 1) fields.Add(InfoField.Spacer);
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
