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

    /// <summary>Shrinks the window if it's taller than the monitor's working area, and keeps it on screen.</summary>
    private void FitToScreen()
    {
        if ((Screens.ScreenFromWindow(this) ?? Screens.Primary) is not { } screen) return;

        var work = screen.WorkingArea;
        var scale = screen.Scaling;
        var frame = FrameSize is { } f ? f.Height - ClientSize.Height : 40;
        var available = work.Height / scale - frame;

        if (Height > available) Height = Math.Max(MinHeight, available);

        var totalPx = (int)Math.Round((Height + frame) * scale);
        var y = Math.Clamp(Position.Y, work.Y, Math.Max(work.Y, work.Bottom - totalPx));
        Position = new PixelPoint(Position.X, y);
    }
}
