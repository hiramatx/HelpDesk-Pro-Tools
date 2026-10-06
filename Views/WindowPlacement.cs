using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

namespace HelpDesk_Pro_Tools.Views;

/// <summary>Snaps a window to the left or right half of a monitor's working area (screen minus taskbar).</summary>
public static class WindowPlacement
{
    // Rough title bar + border size, used before the real frame size is known.
    private const double EstimatedFrameWidth = 16;
    private const double EstimatedFrameHeight = 40;

    public static void SnapToHalf(Window window, Screen? screen, bool left)
    {
        if (screen is null) return;

        var work = screen.WorkingArea; // physical pixels
        var scale = screen.Scaling;
        var frameW = window.FrameSize is { } fw ? fw.Width - window.ClientSize.Width : EstimatedFrameWidth;
        var frameH = window.FrameSize is { } fh ? fh.Height - window.ClientSize.Height : EstimatedFrameHeight;

        var halfPx = work.Width / 2;
        window.Width = Math.Max(window.MinWidth, halfPx / scale - frameW);
        window.Height = Math.Max(window.MinHeight, work.Height / scale - frameH);
        window.Position = new PixelPoint(left ? work.X : work.X + halfPx, work.Y);
    }
}
