using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HelpDesk_Pro_Tools.Services;

namespace HelpDesk_Pro_Tools.ViewModels;

/// <summary>The app's login box: checks the user against AD and, if asked, keeps them logged in.</summary>
public partial class LoginViewModel : ViewModelBase
{
    public LoginViewModel(string? userName, bool keepLoggedIn)
    {
        UserName = userName ?? "";
        KeepLoggedIn = keepLoggedIn;
    }

    /// <summary>Raised on the UI thread after a successful login; the window closes on it.</summary>
    public event Action<AppLogin>? LoggedIn;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LogInCommand))]
    public partial string UserName { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LogInCommand))]
    public partial string Password { get; set; } = "";

    [ObservableProperty]
    public partial bool KeepLoggedIn { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LogInCommand))]
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
        Status = "Checking with Active Directory...";
        try
        {
            var login = await Task.Run(() => AppLoginService.LogIn(user, password));

            // Only the latest choice counts: unticking "Keep me logged in" forgets a saved login.
            await Task.Run(() =>
            {
                if (keep) CredentialStore.Save(login.LoginName, password);
                else CredentialStore.Delete();
            });

            Password = "";
            LoggedIn?.Invoke(login);
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
