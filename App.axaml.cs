using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using HelpDesk_Pro_Tools.Services;
using HelpDesk_Pro_Tools.ViewModels;
using HelpDesk_Pro_Tools.Views;

namespace HelpDesk_Pro_Tools;

public partial class App : Application
{
    private SettingsService _settings = null!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _settings = new SettingsService();
            ThemeService.Apply(_settings.Current.IsDarkTheme ?? ThemeService.IsSystemDark);

            // Started normally (double-click / shortcut) as the Windows user: log in, then start again as
            // that account. Started by the login box (--logged-in) or with "Run as different user": open
            // the tools right away.
            var loggedIn = desktop.Args?.Contains(AppLoginService.LoggedInArgument, StringComparer.OrdinalIgnoreCase) == true;
            if (loggedIn || !WindowsSession.IsAppRunningAsDesktopUser())
                ShowMain(desktop);
            else
                StartLogin(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// With a kept login, starts the app as that account straight away (hold Shift to get the box instead).
    /// Otherwise, or when the kept login no longer works, shows the login box.
    /// </summary>
    private void StartLogin(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var saved = IsShiftDown() ? null : CredentialStore.Load();
        if (saved is not { } kept)
        {
            ShowLogin(desktop, message: null);
            return;
        }

        // Nothing is on screen while the kept login is tried, so keep the app running until it's decided.
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        Dispatcher.UIThread.Post(async () =>
        {
            string? problem;
            try
            {
                await Task.Run(() => AppLoginService.LaunchAs(kept.UserName, kept.Password));
                desktop.Shutdown();
                return;
            }
            catch (AppLoginException ex) when (ex.WrongPassword)
            {
                // Password changed, account locked...: forget it so a stale password isn't tried on every start.
                CredentialStore.Delete();
                _settings.Current.KeepLoggedIn = false;
                _settings.Save();
                problem = $"Your saved login for {kept.UserName} no longer works ({ex.Message.TrimEnd('.')}). Log in again.";
            }
            catch (Exception ex)
            {
                problem = ex.Message;
            }

            desktop.ShutdownMode = ShutdownMode.OnLastWindowClose;
            ShowLogin(desktop, problem);
        });
    }

    private void ShowLogin(IClassicDesktopStyleApplicationLifetime desktop, string? message)
    {
        var vm = new LoginViewModel(_settings, message);
        var window = new LoginWindow { DataContext = vm };

        vm.Launched += () => desktop.Shutdown();
        vm.RunHere += () =>
        {
            ShowMain(desktop);   // open the tools first, so closing the box doesn't end the app
            window.Close();
        };

        desktop.MainWindow = window;
        window.Show();
    }

    private void ShowMain(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var main = new MainWindow
        {
            DataContext = new MainViewModel(_settings, new DialogService(), new ScriptCatalogService()),
        };
        desktop.MainWindow = main;
        main.Show();
    }

    private static bool IsShiftDown() => (GetKeyState(VK_SHIFT) & 0x8000) != 0;

    private const int VK_SHIFT = 0x10;

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int virtualKey);
}
