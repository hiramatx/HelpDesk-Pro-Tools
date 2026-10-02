using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace HelpDesk_Pro_Tools.Services;

/// <summary>
/// Opens folders using the credentials this app is running with.
///
/// File Explorer can't do that when the app is started with "Run as different user": explorer.exe
/// always hands the window to the desktop user's Explorer (its window host is registered to run as
/// "Interactive User"), so \\PC\c$ would open as the logged-on standard user and ask for credentials.
///
/// In that case the folder is shown in the Windows file dialog instead. The dialog is hosted in this
/// process, so it uses this app's account, and it offers the same file list as Explorer (right-click
/// menu, copy / paste, rename, delete, drag and drop). When the app runs as the logged-on user,
/// normal File Explorer is used.
/// </summary>
public static class FolderBrowser
{
    /// <param name="path">Folder to open (local or UNC).</param>
    /// <param name="onError">Called (on a background thread) if the folder can't be opened.</param>
    public static void Open(string path, Action<string>? onError = null)
    {
        if (IsRunningAsDesktopUser())
        {
            var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            Process.Start(new ProcessStartInfo(explorer, $"\"{path}\"") { UseShellExecute = false })?.Dispose();
            return;
        }

        OpenInApp(path, onError);
    }

    /// <summary>Shows the folder in an in-process file dialog on its own thread, so several can be open at once.</summary>
    public static void OpenInApp(string path, Action<string>? onError = null)
    {
        var thread = new Thread(() => RunDialog(path, onError)) { IsBackground = true, Name = "FolderBrowser" };
        thread.SetApartmentState(ApartmentState.STA); // required for shell dialogs
        thread.Start();
    }

    private static void RunDialog(string path, Action<string>? onError)
    {
        try
        {
            var folder = path;
            if (!Directory.Exists(folder))
            {
                // e.g. \\PC\c$\Users\jdoe when that profile doesn't exist: fall back to the parent folder.
                var parent = Path.GetDirectoryName(folder);
                if (parent is null || !Directory.Exists(parent))
                    throw new DirectoryNotFoundException($"Cannot open {path}. Check that the PC is online and that {Environment.UserName} has access.");
                folder = parent;
            }

            // Keep the window open like Explorer: "Open" on a file launches it, then the browser comes back.
            while (true)
            {
                var dialog = (IFileOpenDialog)new FileOpenDialogCoClass();
                try
                {
                    dialog.SetOptions(FOS_NOCHANGEDIR | FOS_FORCEFILESYSTEM | FOS_DONTADDTORECENT | FOS_FORCESHOWHIDDEN);
                    dialog.SetTitle($"{folder}   (as {Environment.UserName})");
                    dialog.SetOkButtonLabel("Open");

                    var iid = typeof(IShellItem).GUID;
                    SHCreateItemFromParsingName(folder, IntPtr.Zero, ref iid, out var start);
                    dialog.SetFolder(start);

                    if (dialog.Show(IntPtr.Zero) != 0) return; // closed / cancelled

                    dialog.GetResult(out var chosen);
                    chosen.GetDisplayName(SIGDN_FILESYSPATH, out var file);
                    dialog.GetFolder(out var current);
                    current.GetDisplayName(SIGDN_FILESYSPATH, out folder);

                    // Opens the file with its default program, still as this app's user.
                    Process.Start(new ProcessStartInfo(file) { UseShellExecute = true })?.Dispose();
                }
                finally
                {
                    Marshal.FinalReleaseComObject(dialog);
                }
            }
        }
        catch (Exception ex)
        {
            // This is a background thread: an exception escaping here would end the whole app.
            try { onError?.Invoke(ex.Message); }
            catch { /* nothing more can be done */ }
        }
    }

    /// <summary>True when this process runs as the user who is logged on to the desktop session.</summary>
    public static bool IsRunningAsDesktopUser()
    {
        try
        {
            var user = QuerySession(WTS_USERNAME);
            var domain = QuerySession(WTS_DOMAINNAME);
            return string.Equals(user, Environment.UserName, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(domain, Environment.UserDomainName, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return true; // can't tell: behave like a normal app
        }
    }

    private static string QuerySession(int infoClass)
    {
        if (!WTSQuerySessionInformation(IntPtr.Zero, WTS_CURRENT_SESSION, infoClass, out var buffer, out _))
            throw new InvalidOperationException("WTSQuerySessionInformation failed");
        try { return Marshal.PtrToStringUni(buffer) ?? ""; }
        finally { WTSFreeMemory(buffer); }
    }

    // ------------------------------------------------------------------ native

    private const int WTS_CURRENT_SESSION = -1;
    private const int WTS_USERNAME = 5;
    private const int WTS_DOMAINNAME = 7;

    private const uint FOS_NOCHANGEDIR = 0x00000008;
    private const uint FOS_FORCEFILESYSTEM = 0x00000040;
    private const uint FOS_DONTADDTORECENT = 0x02000000;
    private const uint FOS_FORCESHOWHIDDEN = 0x10000000;
    private const uint SIGDN_FILESYSPATH = 0x80058000;

    [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool WTSQuerySessionInformation(IntPtr server, int sessionId, int infoClass, out IntPtr buffer, out int bytes);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr memory);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem item);

    [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
    private class FileOpenDialogCoClass { }

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        void GetParent(out IShellItem ppsi);
        void GetDisplayName(uint sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        void Compare(IShellItem psi, uint hint, out int piOrder);
    }

    // Methods must stay in vtable order (IModalWindow, IFileDialog, IFileOpenDialog).
    [ComImport, Guid("d57c7288-d4ad-4768-be02-9d969532d960"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOpenDialog
    {
        [PreserveSig] int Show(IntPtr parent);
        void SetFileTypes(uint cFileTypes, IntPtr rgFilterSpec);
        void SetFileTypeIndex(uint iFileType);
        void GetFileTypeIndex(out uint piFileType);
        void Advise(IntPtr pfde, out uint pdwCookie);
        void Unadvise(uint dwCookie);
        void SetOptions(uint fos);
        void GetOptions(out uint pfos);
        void SetDefaultFolder(IShellItem psi);
        void SetFolder(IShellItem psi);
        void GetFolder(out IShellItem ppsi);
        void GetCurrentSelection(out IShellItem ppsi);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
        void GetResult(out IShellItem ppsi);
        void AddPlace(IShellItem psi, int fdap);
        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
        void Close(int hr);
        void SetClientGuid(ref Guid guid);
        void ClearClientData();
        void SetFilter(IntPtr pFilter);
        void GetResults(out IntPtr ppenum);
        void GetSelectedItems(out IntPtr ppsai);
    }
}
