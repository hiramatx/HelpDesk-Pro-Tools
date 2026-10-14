using System;
using System.Diagnostics;
using System.IO;

namespace HelpDesk_Pro_Tools.Services;

/// <summary>
/// Opens Dockett (github.com/hiramatx/dockett), the dual pane file manager shipped in Tools\Dockett next
/// to the exe (the build downloads its portable release, see the csproj).
///
/// Dockett takes the left and right pane folders as its first two arguments. It is started with
/// UseShellExecute = false, so it runs as the account this app runs as (e.g. d.smith-admin), even when
/// the app was started with "Run as different user".
/// </summary>
public static class DockettLauncher
{
    public static string ExePath => Path.Combine(AppContext.BaseDirectory, "Tools", "Dockett", "Dockett.exe");

    public static bool IsAvailable => File.Exists(ExePath);

    /// <summary>Opens Dockett with <paramref name="left"/> in the left pane and <paramref name="right"/> in the right.</summary>
    public static void Open(string left, string right)
    {
        var psi = new ProcessStartInfo(ExePath) { UseShellExecute = false };
        psi.ArgumentList.Add(left);
        psi.ArgumentList.Add(right);
        Process.Start(psi)?.Dispose();
    }
}
