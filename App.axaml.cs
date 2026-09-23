using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using HelpDesk_Pro_Tools.Services;
using HelpDesk_Pro_Tools.ViewModels;
using HelpDesk_Pro_Tools.Views;

namespace HelpDesk_Pro_Tools;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var settings = new SettingsService();
            var dialogs = new DialogService();
            var scripts = new ScriptCatalogService();

            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel(settings, dialogs, scripts),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
