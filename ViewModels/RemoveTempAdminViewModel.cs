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

/// <summary>Lists temp_admin_users.json and removes the selected users from Administrators on their PC.</summary>
public partial class RemoveTempAdminViewModel : ViewModelBase
{
    public RemoveTempAdminViewModel()
    {
        Reload();
    }

    public ObservableCollection<TempAdminEntry> Entries { get; } = new();

    private List<TempAdminEntry> _selected = new();

    /// <summary>Called by the window when the grid selection changes.</summary>
    public void SetSelection(IEnumerable<TempAdminEntry> selected)
    {
        _selected = selected.ToList();
        RemoveCommand.NotifyCanExecuteChanged();
    }

    public bool IsEmpty => Entries.Count == 0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReloadCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? Status { get; set; }

    [ObservableProperty]
    public partial bool IsError { get; set; }

    private bool CanReload() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanReload))]
    private void Reload()
    {
        IsError = false;
        Status = null;
        Entries.Clear();
        try
        {
            foreach (var entry in TempAdminLog.Load().OrderBy(e => e.Date, StringComparer.Ordinal))
                Entries.Add(entry);
        }
        catch (Exception ex)
        {
            IsError = true;
            Status = $"Could not read {TempAdminLog.FilePath}\n{ex.Message}";
        }
        OnPropertyChanged(nameof(IsEmpty));
    }

    private bool CanRemove() => !IsBusy && _selected.Count > 0;

    [RelayCommand(CanExecute = nameof(CanRemove))]
    private async Task Remove()
    {
        var selected = _selected;
        IsBusy = true;
        IsError = false;
        Status = "Removing...";

        var failed = false;
        var lines = new List<string>();
        foreach (var entry in selected)
        {
            try
            {
                var removed = await Task.Run(() => LocalGroupService.RemoveMember(entry.Pc, "Administrators", entry.User));
                await Task.Run(() => TempAdminLog.Remove(entry.Pc, entry.User));
                lines.Add(removed
                    ? $"Removed {entry.User} from Administrators on {entry.Pc}."
                    : $"{entry.User} was no longer in Administrators on {entry.Pc}; entry removed.");
            }
            catch (Exception ex)
            {
                // Entry stays in the file so it can be retried (e.g. the PC was offline).
                failed = true;
                lines.Add($"{entry.User} on {entry.Pc}: {ex.Message}");
            }
        }

        IsBusy = false;
        Reload();
        if (IsError) return; // the file couldn't be re-read; keep that message
        IsError = failed;
        Status = string.Join(Environment.NewLine, lines);
    }
}
