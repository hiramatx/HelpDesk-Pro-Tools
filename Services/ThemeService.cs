using Avalonia;
using Avalonia.Styling;

namespace HelpDesk_Pro_Tools.Services;

public static class ThemeService
{
    public static bool IsSystemDark =>
        Application.Current?.PlatformSettings?.GetColorValues().ThemeVariant == Avalonia.Platform.PlatformThemeVariant.Dark;

    /// <summary>Applies the theme app-wide, so every open window (and pop-up) follows it.</summary>
    public static void Apply(bool dark)
    {
        if (Application.Current is { } app)
            app.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
    }
}
