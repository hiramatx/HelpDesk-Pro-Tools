using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HelpDesk_Pro_Tools.Services;

namespace HelpDesk_Pro_Tools.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private const int MaxHistory = 30;

    private readonly SettingsService _settings;
    private readonly IDialogService _dialogs;
    private readonly ScriptCatalogService _scriptCatalog;

    public MainViewModel(SettingsService settings, IDialogService dialogs, ScriptCatalogService scriptCatalog)
    {
        _settings = settings;
        _dialogs = dialogs;
        _scriptCatalog = scriptCatalog;

        PcHistory = new ObservableCollection<string>(_settings.Current.PcHistory);

        IsDarkTheme = _settings.Current.IsDarkTheme ?? ThemeService.IsSystemDark;
        ThemeService.Apply(IsDarkTheme);

        LoadScripts();
    }

    // ------------------------------------------------------------------ Header

    // Two different users:
    //   - the account the app runs as (the login box starts the app as it): shown in the header, green
    //     dot when it's in Domain Admins. The remote tools run as this account.
    //   - the Windows desktop user: whose profile c$ Share opens on the left

    /// <summary>The account the app runs as.</summary>
    public string HeaderUserName { get; } = Environment.UserName;

    /// <summary>The user logged on to Windows on this PC (not necessarily the account the app runs as).</summary>
    public string WindowsUserName { get; } = WindowsSession.UserName;

    /// <summary>Green dot when true, red otherwise.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IndicatorToolTip))]
    public partial bool IsDomainAdmin { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IndicatorToolTip))]
    public partial bool IsCheckingDomainAdmin { get; set; } = true;

    /// <summary>Why Domain Admins couldn't be checked (e.g. no domain controller reachable).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IndicatorToolTip))]
    public partial string? DomainAdminProblem { get; set; }

    public string IndicatorToolTip =>
        IsCheckingDomainAdmin ? "Checking Domain Admins membership..."
        : DomainAdminProblem is { } problem ? problem
        : IsDomainAdmin ? $"{HeaderUserName} is a Domain Admin"
        : $"{HeaderUserName} is not a Domain Admin";

    /// <summary>Hover text on the header name.</summary>
    public string UserDetailsToolTip =>
        $"Running as: {Environment.UserDomainName}\\{Environment.UserName}\nWindows user: {WindowsUserName}\n\n" +
        "To log in as another account, close the app and hold Shift while starting it.";

    /// <summary>Called once the main window is open: checks Domain Admins in the background.</summary>
    public async Task StartAsync()
    {
        try
        {
            IsDomainAdmin = await Task.Run(() => AppLoginService.IsCurrentUserDomainAdmin());
        }
        catch (Exception ex)
        {
            DomainAdminProblem = ex is AppLoginException ? ex.Message : $"Could not check Domain Admins: {ex.Message}";
        }
        finally
        {
            IsCheckingDomainAdmin = false;
        }
    }

    [ObservableProperty]
    public partial bool IsDarkTheme { get; set; }

    partial void OnIsDarkThemeChanged(bool value)
    {
        ThemeService.Apply(value);
        _settings.Current.IsDarkTheme = value;
        _settings.Save();
    }

    // ------------------------------------------------------------------ Remote PC

    public ObservableCollection<string> PcHistory { get; }

    /// <summary>
    /// Bound to the editable ComboBox's Text, so both typed names and names picked
    /// from the history list end up here.
    /// </summary>
    [ObservableProperty]
    public partial string? PcName { get; set; }

    [ObservableProperty]
    public partial string? ValidationMessage { get; set; }

    partial void OnPcNameChanged(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            ValidationMessage = null;
    }

    /// <summary>
    /// Validates the current PC name and records it in history.
    /// Captures the name *before* touching the history list: editing the ComboBox's
    /// ItemsSource can make it reset its Text, so the name is put back afterwards.
    /// </summary>
    private bool TryGetPc(out string pc)
    {
        pc = HostName.Normalize(PcName);

        if (pc.Length == 0)
        {
            ValidationMessage = "Enter or select a Remote PC name first.";
            return false;
        }

        if (!HostName.IsValid(pc))
        {
            ValidationMessage = $"\"{pc}\" is not a valid computer name.";
            return false;
        }

        ValidationMessage = null;
        RememberPc(pc);
        return true;
    }

    private void RememberPc(string pc)
    {
        var index = PcHistory.ToList().FindIndex(h => h.Equals(pc, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            PcHistory.Insert(0, pc);
            while (PcHistory.Count > MaxHistory) PcHistory.RemoveAt(PcHistory.Count - 1);
        }
        else if (index > 0)
        {
            PcHistory.Move(index, 0);
        }

        _settings.Current.PcHistory = PcHistory.ToList();
        _settings.Save();

        // Restore the name now and again after the ComboBox has processed the list change.
        PcName = pc;
        Dispatcher.UIThread.Post(() => { if (PcName != pc) PcName = pc; }, DispatcherPriority.Background);
    }

    [RelayCommand]
    private void ClearHistory()
    {
        var current = PcName;
        PcHistory.Clear();
        _settings.Current.PcHistory.Clear();
        _settings.Save();
        PcName = current;
    }

    [RelayCommand]
    private void GetPcDetails()
    {
        if (TryGetPc(out var pc)) _dialogs.ShowPcDetails(pc);
    }

    // ------------------------------------------------------------------ Remote tools

    [RelayCommand]
    private void SfcScan()
    {
        if (TryGetPc(out var pc))
            _dialogs.ShowOutput($"SFC Scan - {pc}", (log, ct) => RemoteOperations.SfcScanAsync(pc, log, ct));
    }

    [RelayCommand]
    private Task RemotePs() => Launch(pc =>
        ProcessLauncher.LaunchPowerShell($"-NoExit -NoProfile -Command \"Enter-PSSession -ComputerName {pc}\""));

    [RelayCommand]
    private void CleanupTemp()
    {
        if (TryGetPc(out var pc))
            _dialogs.ShowOutput($"Cleanup Temp Folders - {pc}", (log, ct) => RemoteOperations.CleanupTempAsync(pc, log, ct));
    }

    [RelayCommand]
    private void DownloadLogs()
    {
        if (TryGetPc(out var pc))
            _dialogs.ShowOutput($"Download Log Files - {pc}", (log, ct) => RemoteOperations.DownloadLogsAsync(pc, log, ct));
    }

    [RelayCommand]
    private void Ping()
    {
        if (TryGetPc(out var pc))
            _dialogs.ShowOutput($"Ping - {pc}", (log, ct) => RemoteOperations.PingAsync(pc, log, ct), cancelText: "Stop");
    }

    [RelayCommand]
    private async Task CShare()
    {
        if (!TryGetPc(out var pc)) return;

        // Moving files between the tech's PC and the target PC, with the account this app runs as:
        //   left  - the Windows logged-in user's profile on this PC - e.g. C:\Users\d.smith
        //   right - the target PC's C: drive                         - \\PC\c$
        var local = WindowsSession.ProfilePath;
        var usersFolder = System.IO.Path.GetDirectoryName(local) ?? @"C:\Users";
        var remote = $@"\\{pc}\c$";

        try
        {
            if (!DockettLauncher.IsAvailable)
            {
                // Dockett missing (e.g. a dev build without it): fall back to two folder windows.
                // Folder problems are reported from the browser's own thread, so hop back to the UI thread.
                void ShowError(string message) =>
                    Dispatcher.UIThread.Post(() => _ = _dialogs.ShowErrorAsync("c$ Share", message));
                ProcessLauncher.OpenFolder(local, ShowError);
                ProcessLauncher.OpenFolder(remote, ShowError);
                return;
            }

            // Dockett skips start folders that don't exist (an offline PC would end up showing this PC's C:),
            // so check both first. A UNC check can take a while when the PC is offline: keep it off the UI thread.
            var (localOk, remoteOk) = await Task.Run(() => (System.IO.Directory.Exists(local), System.IO.Directory.Exists(remote)));
            if (!remoteOk)
            {
                await _dialogs.ShowErrorAsync("c$ Share", $"Cannot open {remote}. Check that the PC is online and that {Environment.UserName} has access.");
                return;
            }
            DockettLauncher.Open(localOk ? local : usersFolder, remote);
        }
        catch (Exception ex)
        {
            await _dialogs.ShowErrorAsync("Launch failed", ex.Message);
        }
    }

    [RelayCommand]
    private Task RemoteAssist() => Launch(pc =>
        ProcessLauncher.Launch(System.IO.Path.Combine(Environment.SystemDirectory, "msra.exe"), $"/offerra {pc}"));

    [RelayCommand]
    private Task RemoteAdmin() => Launch(pc =>
        ProcessLauncher.Launch(System.IO.Path.Combine(Environment.SystemDirectory, "mstsc.exe"), $"/v:{pc} /admin"));

    [RelayCommand]
    private void ProcessManager()
    {
        if (TryGetPc(out var pc)) _dialogs.ShowProcessManager(pc);
    }

    [RelayCommand]
    private Task ComputerManagement() => Launch(pc => ProcessLauncher.LaunchMmc("compmgmt.msc", pc));

    [RelayCommand]
    private async Task RebootPc()
    {
        if (!TryGetPc(out var pc)) return;

        var confirmed = await _dialogs.ConfirmAsync(
            "Reboot PC",
            $"Force an immediate restart of {pc}?\n\nAny logged-in user will lose unsaved work.",
            "Reboot");

        if (confirmed)
            _dialogs.ShowOutput($"Reboot - {pc}", (log, ct) => RemoteOperations.RebootAsync(pc, log, ct));
    }

    [RelayCommand]
    private void AddTempAdmin()
    {
        if (TryGetPc(out var pc)) _dialogs.ShowAddTempAdmin(pc);
    }

    /// <summary>Works from the log file, which names each entry's PC, so no PC name is needed here.</summary>
    [RelayCommand]
    private void RemoveTempAdmin() => _dialogs.ShowRemoveTempAdmin();

    [RelayCommand]
    private void AddRemoteDesktop()
    {
        if (TryGetPc(out var pc)) _dialogs.ShowAddRemoteDesktop(pc);
    }

    [RelayCommand]
    private void SendMessage()
    {
        if (TryGetPc(out var pc)) _dialogs.ShowSendMessage(pc);
    }

    /// <summary>Validates the PC, then runs a launcher and reports launch failures.</summary>
    private async Task Launch(Action<string> start)
    {
        if (!TryGetPc(out var pc)) return;
        try
        {
            start(pc);
        }
        catch (Exception ex)
        {
            await _dialogs.ShowErrorAsync("Launch failed", ex.Message);
        }
    }

    // ------------------------------------------------------------------ Local tools

    [RelayCommand]
    private Task AdConsole() => LaunchLocal(() => ProcessLauncher.LaunchMmc("dsa.msc"));

    [RelayCommand]
    private Task WindowsTerminal() => LaunchLocal(() => ProcessLauncher.Launch("wt.exe"));

    private async Task LaunchLocal(Action start)
    {
        try
        {
            start();
        }
        catch (Exception ex)
        {
            await _dialogs.ShowErrorAsync("Launch failed", ex.Message);
        }
    }

    // ------------------------------------------------------------------ Scripts

    public ObservableCollection<ScriptCategoryViewModel> ScriptTabs { get; } = new();

    [ObservableProperty]
    public partial ScriptCategoryViewModel? SelectedScriptTab { get; set; }

    [RelayCommand]
    private void LoadScripts()
    {
        var selected = SelectedScriptTab?.Name;
        ScriptTabs.Clear();

        try
        {
            foreach (var (tab, scripts) in _scriptCatalog.Load())
                ScriptTabs.Add(new ScriptCategoryViewModel(tab, scripts.Select(s => new ScriptItemViewModel(s, RunScript))));
        }
        catch (Exception ex)
        {
            _ = _dialogs.ShowErrorAsync("scripts.json", $"Could not read {_scriptCatalog.CatalogPath}\n\n{ex.Message}");
            foreach (var tab in ScriptCatalogService.DefaultTabs)
                ScriptTabs.Add(new ScriptCategoryViewModel(tab, Enumerable.Empty<ScriptItemViewModel>()));
        }

        SelectedScriptTab = ScriptTabs.FirstOrDefault(t => t.Name == selected) ?? ScriptTabs.FirstOrDefault();
    }

    [RelayCommand]
    private Task OpenScriptCatalog() => LaunchLocal(() => ProcessLauncher.Launch("notepad.exe", $"\"{_scriptCatalog.CatalogPath}\""));

    private async Task RunScript(ScriptItemViewModel script)
    {
        if (!System.IO.File.Exists(script.Path))
        {
            await _dialogs.ShowErrorAsync("Script not found", script.Path);
            return;
        }

        var args = script.Arguments;
        if (args?.Contains("{PC}", StringComparison.OrdinalIgnoreCase) == true)
        {
            if (!TryGetPc(out var pc)) return;
            args = args.Replace("{PC}", pc, StringComparison.OrdinalIgnoreCase);
        }

        await LaunchLocal(() => ProcessLauncher.LaunchScript(script.Path, args));
    }
}
