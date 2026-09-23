using System;
using System.Threading;
using System.Threading.Tasks;

namespace HelpDesk_Pro_Tools.Services;

/// <summary>Lets view models open windows without referencing views.</summary>
public interface IDialogService
{
    /// <summary>Opens a live output window and runs <paramref name="work"/>, streaming its log lines into it.</summary>
    void ShowOutput(string title, Func<Action<string>, CancellationToken, Task> work);

    void ShowPcDetails(string pc);

    void ShowSendMessage(string pc);

    Task<bool> ConfirmAsync(string title, string message, string confirmText);

    Task ShowErrorAsync(string title, string message);
}
