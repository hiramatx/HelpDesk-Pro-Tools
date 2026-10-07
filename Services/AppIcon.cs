using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Platform;

namespace HelpDesk_Pro_Tools.Services;

/// <summary>
/// The window / taskbar icon. Uses Resources\hdpt.ico next to the exe when it's there (so the icon can be
/// swapped without rebuilding), otherwise the built-in Assets icon.
/// </summary>
public static class AppIcon
{
    public static string FilePath { get; } = Path.Combine(AppContext.BaseDirectory, "Resources", "hdpt.ico");

    private const string FallbackUri = "avares://HelpDeskProTools/Assets/avalonia-logo.ico";

    public static WindowIcon Current { get; } = Load();

    private static WindowIcon Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return new WindowIcon(FilePath);
        }
        catch
        {
            // Unreadable or not a valid .ico: use the built-in icon instead.
        }

        return new WindowIcon(AssetLoader.Open(new Uri(FallbackUri)));
    }
}
