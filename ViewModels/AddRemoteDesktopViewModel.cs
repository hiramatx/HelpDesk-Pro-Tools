using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HelpDesk_Pro_Tools.Services;

namespace HelpDesk_Pro_Tools.ViewModels;

/// <summary>Adds a user to Remote Desktop Users and/or Direct Access Users on the PC.</summary>
public partial class AddRemoteDesktopViewModel : ViewModelBase
{
    public const string RemoteDesktopGroup = "Remote Desktop Users";

    public AddRemoteDesktopViewModel(string pcName)
    {
        PcName = pcName;
    }

    public string PcName { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    public partial string UserName { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    public partial bool AddRemoteDesktopUser { get; set; } = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    public partial bool AddDirectAccessUser { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? Status { get; set; }

    [ObservableProperty]
    public partial bool IsError { get; set; }

    private bool CanAdd() =>
        !IsBusy && !string.IsNullOrWhiteSpace(UserName) && (AddRemoteDesktopUser || AddDirectAccessUser);

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private async Task Add()
    {
        var user = UserName.Trim();
        var groups = new List<string>();
        if (AddRemoteDesktopUser) groups.Add(RemoteDesktopGroup);
        if (AddDirectAccessUser) groups.Add(PcInfoService.DirectAccessGroupName);

        IsBusy = true;
        IsError = false;
        Status = $"Adding {user} on {PcName}...";

        // Each group is tried on its own, so one failing doesn't stop the other.
        var lines = new List<string>();
        foreach (var group in groups)
        {
            try
            {
                var added = await Task.Run(() => LocalGroupService.AddMember(PcName, group, user));
                lines.Add(added ? $"Added {user} to {group}." : $"{user} is already in {group}.");
            }
            catch (Exception ex)
            {
                IsError = true;
                lines.Add($"{group}: {ex.Message}");
            }
        }

        Status = string.Join(Environment.NewLine, lines);
        IsBusy = false;
    }
}
