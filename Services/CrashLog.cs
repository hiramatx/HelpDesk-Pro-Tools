using System;
using System.IO;

namespace HelpDesk_Pro_Tools.Services;

/// <summary>Records unexpected errors in %AppData%\HelpDeskProTools\crash.log so they can be diagnosed afterwards.</summary>
public static class CrashLog
{
    private const long MaxBytes = 512 * 1024;

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HelpDeskProTools", "crash.log");

    public static void Write(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            if (File.Exists(FilePath) && new FileInfo(FilePath).Length > MaxBytes)
                File.Delete(FilePath);
            File.AppendAllText(FilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never cause a second failure.
        }
    }
}
