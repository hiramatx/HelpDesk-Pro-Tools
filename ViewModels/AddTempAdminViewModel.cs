using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HelpDesk_Pro_Tools.Services;

namespace HelpDesk_Pro_Tools.ViewModels;

/// <summary>Adds a user to the PC's Administrators group and logs it in temp_admin_users.json.</summary>
public partial class AddTempAdminViewModel : ViewModelBase
{
    public AddTempAdminViewModel(string pcName)
    {
        PcName = pcName;
    }

    public string PcName { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    public partial string UserName { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? Status { get; set; }

    [ObservableProperty]
    public partial bool IsError { get; set; }

    private bool CanAdd() => !IsBusy && !string.IsNullOrWhiteSpace(UserName);

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private async Task Add()
    {
        var user = UserName.Trim();
        IsBusy = true;
        IsError = false;
        Status = $"Adding {user} to Administrators on {PcName}...";
        try
        {
            var added = await Task.Run(() => LocalGroupService.AddMember(PcName, "Administrators", user));
            if (added)
            {
                await Task.Run(() => TempAdminLog.Add(PcName, user));
                Status = $"{user} is now a temporary admin on {PcName} (logged in {TempAdminLog.FileName}).";
            }
            else
            {
                // Not logged: they were an admin before, so "Remove Temporary Admin" shouldn't take it away.
                Status = $"{user} is already in Administrators on {PcName}. Nothing was changed or logged.";
            }
        }
        catch (Exception ex)
        {
            IsError = true;
            Status = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
