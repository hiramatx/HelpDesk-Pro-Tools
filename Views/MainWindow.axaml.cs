using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using HelpDesk_Pro_Tools.ViewModels;

namespace HelpDesk_Pro_Tools.Views;

public partial class MainWindow : Window
{
    // Rough title bar + border height, used before the real frame size is known.
    private const double EstimatedFrameHeight = 40;

    public MainWindow()
    {
        InitializeComponent();

        // Enter in the PC box opens PC Details - but not when Enter is just picking
        // an entry from the open history drop-down. Tunnel so we see it before the ComboBox.
        PcComboBox.AddHandler(KeyDownEvent, PcComboBox_KeyDown, RoutingStrategies.Tunnel);

        // Size close to the monitor before the first show (avoids a visible jump),
        // then fit exactly once the real frame size is known.
        FitHeightToScreen(Screens.Primary, EstimatedFrameHeight);
        Opened += (_, _) => FitHeightToScreen(Screens.ScreenFromWindow(this) ?? Screens.Primary, null);
    }

    /// <summary>Makes the window as tall as the monitor's working area (screen minus taskbar).</summary>
    private void FitHeightToScreen(Screen? screen, double? frameHeight)
    {
        if (screen is null) return;

        var work = screen.WorkingArea; // physical pixels
        var scale = screen.Scaling;
        var frame = frameHeight ?? (FrameSize is { } f ? f.Height - ClientSize.Height : EstimatedFrameHeight);

        Height = Math.Max(MinHeight, work.Height / scale - frame);

        if (frameHeight is null)
        {
            // Keep the whole window on screen, centred vertically on the working area.
            var totalPx = (int)Math.Round((Height + frame) * scale);
            Position = new PixelPoint(Position.X, work.Y + Math.Max(0, (work.Height - totalPx) / 2));
        }
    }

    private void PcComboBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || PcComboBox.IsDropDownOpen) return;

        if (DataContext is MainViewModel vm && vm.GetPcDetailsCommand.CanExecute(null))
        {
            vm.GetPcDetailsCommand.Execute(null);
            e.Handled = true;
        }
    }
}
