using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HelpDesk_Pro_Tools.Services;

namespace HelpDesk_Pro_Tools.ViewModels;

/// <summary>Lists the processes on a remote PC and ends the selected one.</summary>
public partial class ProcessManagerViewModel : ViewModelBase
{
    private readonly RemoteProcessManager _manager;
    private readonly Func<string, string, string, Task<bool>> _confirm;
    private List<RemoteProcessInfo> _all = new();

    /// <param name="confirm">Shows a confirm dialog (title, message, confirm button text).</param>
    public ProcessManagerViewModel(string pcName, Func<string, string, string, Task<bool>> confirm)
    {
        PcName = pcName;
        _manager = new RemoteProcessManager(pcName);
        _confirm = confirm;
    }

    public string PcName { get; }

    public string Title => $"Process Manager - {PcName}";

    public ObservableCollection<RemoteProcessInfo> Processes { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EndTaskCommand))]
    public partial RemoteProcessInfo? SelectedProcess { get; set; }

    /// <summary>Hidden by default; see RemoteProcessManager for what counts as a Microsoft service.</summary>
    [ObservableProperty]
    public partial bool ShowMicrosoftServices { get; set; }

    partial void OnShowMicrosoftServicesChanged(bool value) => ApplyFilter();

    [ObservableProperty]
    public partial string? SearchText { get; set; }

    partial void OnSearchTextChanged(string? value) => ApplyFilter();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(EndTaskCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? Status { get; set; }

    [ObservableProperty]
    public partial bool IsError { get; set; }

    [ObservableProperty]
    public partial string? Summary { get; set; }

    // ------------------------------------------------------------------ Sorting

    private string _sortColumn = "Name";
    private bool _sortDescending;

    [RelayCommand]
    private void SortBy(string column)
    {
        if (_sortColumn == column)
        {
            _sortDescending = !_sortDescending;
        }
        else
        {
            _sortColumn = column;
            // Biggest memory users first is the useful default for that column.
            _sortDescending = column == "Memory";
        }
        ApplyFilter();
    }

    private IEnumerable<RemoteProcessInfo> Sort(IEnumerable<RemoteProcessInfo> items)
    {
        var cmp = StringComparer.OrdinalIgnoreCase;
        IOrderedEnumerable<RemoteProcessInfo> sorted = _sortColumn switch
        {
            "Pid" => _sortDescending ? items.OrderByDescending(p => p.Pid) : items.OrderBy(p => p.Pid),
            "User" => _sortDescending ? items.OrderByDescending(p => p.User ?? "", cmp) : items.OrderBy(p => p.User ?? "", cmp),
            "Memory" => _sortDescending ? items.OrderByDescending(p => p.WorkingSet) : items.OrderBy(p => p.WorkingSet),
            "Details" => _sortDescending ? items.OrderByDescending(p => p.Details, cmp) : items.OrderBy(p => p.Details, cmp),
            _ => _sortDescending ? items.OrderByDescending(p => p.Name, cmp) : items.OrderBy(p => p.Name, cmp),
        };
        return sorted.ThenBy(p => p.Name, cmp).ThenBy(p => p.Pid);
    }

    // ------------------------------------------------------------------ Loading

    private bool CanRefresh() => !IsBusy;

    public Task RefreshAsync() => RefreshCommand.ExecuteAsync(null);

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task Refresh()
    {
        IsBusy = true;
        IsError = false;
        Status = $"Loading processes from {PcName}...";
        try
        {
            var watch = Stopwatch.StartNew();
            _all = await Task.Run(() => _manager.List());
            Status = $"Loaded at {DateTime.Now:HH:mm:ss} in {watch.Elapsed.TotalSeconds:0.0} s.";
            ApplyFilter();
        }
        catch (Exception ex)
        {
            IsError = true;
            Status = $"Could not read processes from {PcName}: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyFilter()
    {
        var selectedPid = SelectedProcess?.Pid;
        var search = SearchText?.Trim();

        var visible = _all.Where(p => ShowMicrosoftServices || !p.IsMicrosoftService);
        if (!string.IsNullOrEmpty(search))
        {
            visible = visible.Where(p =>
                p.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                || p.PidText == search
                || (p.User?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
                || p.Details.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        Processes.Clear();
        foreach (var p in Sort(visible))
            Processes.Add(p);

        SelectedProcess = Processes.FirstOrDefault(p => p.Pid == selectedPid);

        var hidden = _all.Count(p => p.IsMicrosoftService);
        Summary = ShowMicrosoftServices || hidden == 0
            ? $"{Processes.Count} of {_all.Count} processes"
            : $"{Processes.Count} of {_all.Count} processes ({hidden} Microsoft services hidden)";
    }

    // ------------------------------------------------------------------ End task

    private bool CanEndTask() => !IsBusy && SelectedProcess is not null;

    [RelayCommand(CanExecute = nameof(CanEndTask))]
    private async Task EndTask()
    {
        if (SelectedProcess is not { } proc) return;

        var owner = string.IsNullOrEmpty(proc.User) ? "" : $" (running as {proc.User})";
        var warning = proc.IsMicrosoftService
            ? "\n\nThis is a Windows / Microsoft service process. Ending it can crash or restart the PC."
            : "\n\nAny unsaved work in it will be lost.";
        var confirmed = await _confirm(
            "End task",
            $"End {proc.Name}, PID {proc.Pid}{owner}, on {PcName}?{warning}",
            "End task");
        if (!confirmed) return;

        IsBusy = true;
        IsError = false;
        Status = $"Ending {proc.Name}...";
        try
        {
            await Task.Run(() => _manager.Kill(proc.Pid, proc.Name));
        }
        catch (Exception ex)
        {
            IsBusy = false;
            IsError = true;
            Status = ex.Message;
            return;
        }

        IsBusy = false;
        await Refresh();
        if (!IsError)
            Status = $"Ended {proc.Name} (PID {proc.Pid}) at {DateTime.Now:HH:mm:ss}.";
    }
}
