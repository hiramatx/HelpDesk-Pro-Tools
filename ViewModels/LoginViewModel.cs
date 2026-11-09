using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HelpDesk_Pro_Tools.Services;

namespace HelpDesk_Pro_Tools.ViewModels;

/// <summary>
/// The login box shown when the app is started normally (double-click / shortcut). Logging in starts
/// the app again as that account, like "Run as different user".
/// </summary>
public partial class LoginViewModel : ViewModelBase
{
    private readonly SettingsService _settings;

    public LoginViewModel(SettingsService settings, string? message = null)
    {
        _settings = settings;
        UserName = settings.Current.LastLoginName ?? "";
        KeepLoggedIn = settings.Current.KeepLoggedIn;
        if (message is not null)
        {
            Status = message;
            IsError = true;
        }
    }

    /// <summary>The user logged on to Windows, offered as "Run as ..." to skip the login.</summary>
    public string RunAsWindowsUserText { get; } = $"Run as {Environment.UserName} instead";

    /// <summary>Raised on the UI thread once the app was started as the login account: this copy should exit.</summary>
    public event Action? Launched;

    /// <summary>Raised when the main window should open in this process (as the Windows user).</summary>
    public event Action? RunHere;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LogInCommand))]
    public partial string UserName { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LogInCommand))]
    public partial string Password { get; set; } = "";

    [ObservableProperty]
    public partial bool KeepLoggedIn { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LogInCommand), nameof(RunAsWindowsUserCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? Status { get; set; }

    [ObservableProperty]
    public partial bool IsError { get; set; }

    private bool CanLogIn() => !IsBusy && !string.IsNullOrWhiteSpace(UserName) && Password.Length > 0;

    [RelayCommand(CanExecute = nameof(CanLogIn))]
    private async Task LogIn()
    {
        var user = UserName.Trim();
        var password = Password;
        var keep = KeepLoggedIn;

        IsBusy = true;
        IsError = false;
        try
        {
            if (AppLoginService.IsCurrentUser(user))
            {
                // Logging in as the Windows user: nothing to restart, just open the app here.
                Remember(user, keep: false);
                await Task.Run(() => CredentialStore.Delete());
                RunHere?.Invoke();
                return;
            }

            Status = $"Starting HelpDesk Pro Tools as {user}...";
            await Task.Run(() => AppLoginService.LaunchAs(user, password));

            // Only the latest choice counts: unticking "Keep me logged in" forgets a saved login.
            // The app is already running as that account, so a save failure only means the box shows next time.
            await Task.Run(() =>
            {
                try
                {
                    if (keep) CredentialStore.Save(user, password);
                    else CredentialStore.Delete();
                }
                catch { keep = false; }
            });
            Remember(user, keep);
            Password = "";
            Launched?.Invoke();
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

    private bool CanRunAsWindowsUser() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRunAsWindowsUser))]
    private void RunAsWindowsUser() => RunHere?.Invoke();

    private void Remember(string user, bool keep)
    {
        _settings.Current.LastLoginName = user;
        _settings.Current.KeepLoggedIn = keep;
        _settings.Save();
    }
}
