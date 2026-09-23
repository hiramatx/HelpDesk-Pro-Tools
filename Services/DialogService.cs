using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using HelpDesk_Pro_Tools.ViewModels;
using HelpDesk_Pro_Tools.Views;

namespace HelpDesk_Pro_Tools.Services;

public class DialogService : IDialogService
{
    private static Window? Owner =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    public void ShowOutput(string title, Func<Action<string>, CancellationToken, Task> work)
    {
        var vm = new OutputViewModel(title, work);
        var window = new OutputWindow { DataContext = vm };
        Show(window);
        _ = vm.RunAsync();
    }

    public void ShowPcDetails(string pc)
    {
        var vm = new PcDetailsViewModel(pc);
        Show(new PcDetailsWindow { DataContext = vm });
        _ = vm.RefreshAsync();
    }

    public void ShowSendMessage(string pc)
    {
        Show(new SendMessageWindow { DataContext = new SendMessageViewModel(pc) });
    }

    public async Task<bool> ConfirmAsync(string title, string message, string confirmText)
    {
        var dialog = MessageDialog.CreateConfirm(title, message, confirmText, danger: true);
        return Owner is { } owner && await dialog.ShowDialog<bool>(owner);
    }

    public async Task ShowErrorAsync(string title, string message)
    {
        var dialog = MessageDialog.CreateError(title, message);
        if (Owner is { } owner) await dialog.ShowDialog(owner);
        else dialog.Show();
    }

    // Tool windows are non-modal so several jobs / PCs can run side by side.
    private static void Show(Window window)
    {
        if (Owner is { } owner) window.Show(owner);
        else window.Show();
    }
}
