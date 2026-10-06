using System;
using Avalonia;
using Avalonia.Controls;

namespace HelpDesk_Pro_Tools.Views;

public partial class PcDetailsWindow : Window
{
    public PcDetailsWindow()
    {
        InitializeComponent();
        Opened += (_, _) => FitToScreen();
    }

    /// <summary>
    /// Anchors the window to the top-left (0,0) of the working area of the monitor it opened on
    /// (the main window's monitor), shrinking it if it's taller than that working area.
    /// </summary>
    private void FitToScreen()
    {
        if ((Screens.ScreenFromWindow(this) ?? Screens.Primary) is not { } screen) return;

        var work = screen.WorkingArea;
        var scale = screen.Scaling;
        var frame = FrameSize is { } f ? f.Height - ClientSize.Height : 40;
        var available = work.Height / scale - frame;

        if (Height > available) Height = Math.Max(MinHeight, available);

        Position = work.TopLeft;
    }
}
