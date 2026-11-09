using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;

namespace HelpDesk_Pro_Tools.Services;

/// <summary>
/// Who is logged on to this Windows desktop session, as opposed to the account this app runs as.
/// They differ when the app is started with "Run as different user" (e.g. desktop user d.smith,
/// app running as d.smith-admin).
/// </summary>
public static class WindowsSession
{
    /// <summary>The desktop session's user name (no domain). Falls back to the app's account if it can't be read.</summary>
    public static string UserName
    {
        get
        {
            try
            {
                var user = Query(WTS_USERNAME);
                return string.IsNullOrWhiteSpace(user) ? Environment.UserName : user;
            }
            catch
            {
                return Environment.UserName;
            }
        }
    }

    /// <summary>
    /// The desktop user's profile folder on this PC (e.g. C:\Users\d.smith), read from the ProfileList registry
    /// key so profiles named like "d.smith.CORP" are found too. Falls back to C:\Users\&lt;user name&gt;.
    /// </summary>
    public static string ProfilePath
    {
        get
        {
            var user = UserName;
            try
            {
                var domain = Query(WTS_DOMAINNAME);
                var account = string.IsNullOrEmpty(domain) ? user : $@"{domain}\{user}";
                var sid = new NTAccount(account).Translate(typeof(SecurityIdentifier)).Value;
                using var key = Registry.LocalMachine.OpenSubKey($@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\{sid}");
                if (key?.GetValue("ProfileImagePath") is string path && path.Length > 0)
                    return Environment.ExpandEnvironmentVariables(path);
            }
            catch
            {
                // no profile on this PC yet, or the account can't be resolved: use the usual location
            }

            var usersFolder = Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)) ?? @"C:\Users";
            return Path.Combine(usersFolder, user);
        }
    }

    /// <summary>True when this process runs as the user who is logged on to the desktop session.</summary>
    public static bool IsAppRunningAsDesktopUser()
    {
        try
        {
            return string.Equals(Query(WTS_USERNAME), Environment.UserName, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(Query(WTS_DOMAINNAME), Environment.UserDomainName, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return true; // can't tell: behave like a normal app
        }
    }

    private static string Query(int infoClass)
    {
        if (!WTSQuerySessionInformation(IntPtr.Zero, WTS_CURRENT_SESSION, infoClass, out var buffer, out _))
            throw new InvalidOperationException("WTSQuerySessionInformation failed");
        try { return Marshal.PtrToStringUni(buffer) ?? ""; }
        finally { WTSFreeMemory(buffer); }
    }

    private const int WTS_CURRENT_SESSION = -1;
    private const int WTS_USERNAME = 5;
    private const int WTS_DOMAINNAME = 7;

    [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool WTSQuerySessionInformation(IntPtr server, int sessionId, int infoClass, out IntPtr buffer, out int bytes);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr memory);
}
