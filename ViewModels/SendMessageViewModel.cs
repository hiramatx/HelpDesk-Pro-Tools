using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HelpDesk_Pro_Tools.Services;

namespace HelpDesk_Pro_Tools.ViewModels;

public partial class SendMessageViewModel : ViewModelBase
{
    public SendMessageViewModel(string pcName)
    {
        PcName = pcName;
    }

    public string PcName { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial string Message { get; set; } = "";

    /// <summary>How long the message stays on screen (msg.exe /time).</summary>
    [ObservableProperty]
    public partial decimal? DisplayMinutes { get; set; } = 5;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial bool IsSending { get; set; }

    [ObservableProperty]
    public partial string? Status { get; set; }

    [ObservableProperty]
    public partial bool IsError { get; set; }

    private bool CanSend() => !IsSending && !string.IsNullOrWhiteSpace(Message);

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task Send()
    {
        IsSending = true;
        Status = "Sending...";
        IsError = false;
        try
        {
            var seconds = (int)Math.Clamp((DisplayMinutes ?? 5) * 60, 10, 86400);
            var (ok, output) = await RemoteOperations.SendMessageAsync(PcName, Message.Trim(), seconds);
            IsError = !ok;
            Status = ok
                ? $"Message sent to {PcName} at {DateTime.Now:HH:mm}."
                : $"msg.exe failed: {(string.IsNullOrEmpty(output) ? "unknown error" : output)}";
        }
        catch (Exception ex)
        {
            IsError = true;
            Status = ex.Message;
        }
        finally
        {
            IsSending = false;
        }
    }
}
