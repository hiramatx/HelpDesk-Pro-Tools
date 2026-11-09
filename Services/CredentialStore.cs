using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace HelpDesk_Pro_Tools.Services;

/// <summary>
/// Saves the "Keep me logged in" credentials in Windows Credential Manager (Control Panel >
/// Credential Manager > Windows Credentials, entry "HelpDeskProTools/AppLogin").
///
/// Windows encrypts the password with DPAPI for the Windows account the app runs as, so only that
/// account on this PC can read it back. Nothing is written to settings.json or any other file.
/// </summary>
public static class CredentialStore
{
    private const string Target = "HelpDeskProTools/AppLogin";

    public static void Save(string userName, string password)
    {
        var blob = Encoding.Unicode.GetBytes(password);
        var blobPtr = Marshal.AllocHGlobal(blob.Length);
        try
        {
            Marshal.Copy(blob, 0, blobPtr, blob.Length);
            var credential = new CREDENTIAL
            {
                Type = CRED_TYPE_GENERIC,
                TargetName = Target,
                UserName = userName,
                CredentialBlob = blobPtr,
                CredentialBlobSize = (uint)blob.Length,
                Persist = CRED_PERSIST_LOCAL_MACHINE,
                Comment = "HelpDesk Pro Tools - Keep me logged in",
            };
            if (!CredWrite(ref credential, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not save the login in Windows Credential Manager.");
        }
        finally
        {
            // Don't leave the password lying around in unmanaged memory.
            Marshal.Copy(new byte[blob.Length], 0, blobPtr, blob.Length);
            Array.Clear(blob);
            Marshal.FreeHGlobal(blobPtr);
        }
    }

    /// <summary>The saved user name and password, or null when nothing is saved.</summary>
    public static (string UserName, string Password)? Load()
    {
        try
        {
            if (!CredRead(Target, CRED_TYPE_GENERIC, 0, out var ptr)) return null;
            try
            {
                var credential = Marshal.PtrToStructure<CREDENTIAL>(ptr);
                if (string.IsNullOrEmpty(credential.UserName) || credential.CredentialBlobSize == 0) return null;

                var blob = new byte[credential.CredentialBlobSize];
                Marshal.Copy(credential.CredentialBlob, blob, 0, blob.Length);
                var password = Encoding.Unicode.GetString(blob);
                Array.Clear(blob);
                return (credential.UserName, password);
            }
            finally
            {
                CredFree(ptr);
            }
        }
        catch
        {
            return null; // unreadable entry: treat as "not saved"
        }
    }

    public static void Delete()
    {
        try { CredDelete(Target, CRED_TYPE_GENERIC, 0); }
        catch { /* nothing saved */ }
    }

    // ------------------------------------------------------------------ native

    private const uint CRED_TYPE_GENERIC = 1;
    private const uint CRED_PERSIST_LOCAL_MACHINE = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref CREDENTIAL credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);
}
