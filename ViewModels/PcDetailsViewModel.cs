using System;
using System.Collections.ObjectModel;
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

    [ObservableProperty] public partial string PcOu { get; set; } = "";
    [ObservableProperty] public partial string OperatingSystem { get; set; } = "";
    [ObservableProperty] public partial string Uptime { get; set; } = "";

    [ObservableProperty] public partial double CpuPercent { get; set; }
    [ObservableProperty] public partial double RamPercent { get; set; }
    [ObservableProperty] public partial string RamText { get; set; } = "";
    [ObservableProperty] public partial double DiskPercent { get; set; }
    [ObservableProperty] public partial string DiskText { get; set; } = "";

    [ObservableProperty] public partial string LoggedInUser { get; set; } = "";
    [ObservableProperty] public partial string LoggedInUserOu { get; set; } = "";

    [ObservableProperty] public partial bool HasDeviceErrors { get; set; }
    [ObservableProperty] public partial string? Warnings { get; set; }

    public ObservableCollection<VideoCardInfo> VideoCards { get; } = new();
    public ObservableCollection<MonitorInfo> Monitors { get; } = new();
    public ObservableCollection<DeviceError> DeviceErrors { get; } = new();

    private bool CanRefresh() => !IsLoading;

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    public async Task RefreshAsync()
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var d = await PcInfoService.GetDetailsAsync(PcName);

            PcOu = d.PcOu;
            OperatingSystem = d.OperatingSystem;
            Uptime = d.LastBoot is { } boot ? FormatUptime(DateTime.Now - boot) : "";

            CpuPercent = d.CpuPercent;
            RamPercent = d.RamPercent;
            RamText = $"{d.RamUsedGb:0.0} / {d.RamTotalGb:0.0} GB";
            DiskPercent = d.DiskPercent;
            DiskText = $"{d.DiskUsedGb:0} / {d.DiskTotalGb:0} GB  (C:)";

            LoggedInUser = d.LoggedInUser;
            LoggedInUserOu = d.LoggedInUserOu;

            Reset(VideoCards, d.VideoCards);
            Reset(Monitors, d.Monitors);
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

    private static void Reset<T>(ObservableCollection<T> target, System.Collections.Generic.IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items) target.Add(item);
    }

    private static string FormatUptime(TimeSpan t) =>
        t.TotalDays >= 1 ? $"Up {(int)t.TotalDays}d {t.Hours}h" : $"Up {t.Hours}h {t.Minutes}m";
}
