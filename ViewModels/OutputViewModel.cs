using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace HelpDesk_Pro_Tools.ViewModels;

/// <summary>Backs the live output pop-up used by every long-running remote tool.</summary>
public partial class OutputViewModel : ViewModelBase
{
    private readonly Func<Action<string>, CancellationToken, Task> _work;
    private readonly CancellationTokenSource _cts = new();
    private readonly StringBuilder _log = new();

    // Long-running jobs (continuous ping) keep only the most recent output.
    private const int MaxLogChars = 400_000;
    private const int TrimLogChars = 100_000;

    public OutputViewModel(string title, Func<Action<string>, CancellationToken, Task> work, string cancelText = "Cancel")
    {
        Title = title;
        _work = work;
        CancelText = cancelText;
    }

    public string Title { get; }

    /// <summary>Label for the cancel button ("Stop" for jobs that run until stopped).</summary>
    public string CancelText { get; }

    [ObservableProperty]
    public partial string LogText { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    public partial bool IsRunning { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; } = "Starting...";

    [ObservableProperty]
    public partial bool Succeeded { get; set; }

    [ObservableProperty]
    public partial bool Failed { get; set; }

    public async Task RunAsync()
    {
        IsRunning = true;
        Status = "Running...";
        var sw = Stopwatch.StartNew();
        Append($"[{DateTime.Now:HH:mm:ss}] {Title}");
        Append(new string('-', 60));

        try
        {
            await _work(line => Dispatcher.UIThread.Post(() => Append(line)), _cts.Token);
            Status = $"Completed in {sw.Elapsed:mm\\:ss}";
            Succeeded = true;
        }
        catch (OperationCanceledException)
        {
            Status = "Cancelled";
            Append("*** Cancelled ***");
            Failed = true;
        }
        catch (Exception ex)
        {
            Status = "Failed";
            Append("");
            Append($"ERROR: {ex.Message}");
            Failed = true;
        }
        finally
        {
            IsRunning = false;
        }
    }

    private void Append(string line)
    {
        _log.AppendLine(line);
        if (_log.Length > MaxLogChars)
        {
            // Drop the oldest text, cutting at a line break.
            var cut = _log.ToString(TrimLogChars, _log.Length - TrimLogChars).IndexOf('\n');
            _log.Remove(0, TrimLogChars + cut + 1);
        }
        LogText = _log.ToString();
    }

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private void Cancel() => _cts.Cancel();

    /// <summary>Called when the window closes so background work doesn't keep running.</summary>
    public void Stop()
    {
        if (IsRunning) _cts.Cancel();
    }
}
