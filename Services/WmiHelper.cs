using System;
using System.Collections.Generic;
using System.Management;

namespace HelpDesk_Pro_Tools.Services;

public static class WmiHelper
{
    public static ManagementScope Connect(string pc, string wmiNamespace = @"root\cimv2", bool enablePrivileges = false)
    {
        var options = new ConnectionOptions
        {
            Impersonation = ImpersonationLevel.Impersonate,
            Authentication = AuthenticationLevel.PacketPrivacy,
            EnablePrivileges = enablePrivileges,
            Timeout = TimeSpan.FromSeconds(20),
        };
        var scope = new ManagementScope($@"\\{pc}\{wmiNamespace}", options);
        scope.Connect();
        return scope;
    }

    public static List<ManagementBaseObject> Query(ManagementScope scope, string wql)
    {
        var options = new EnumerationOptions { Timeout = TimeSpan.FromSeconds(30), ReturnImmediately = false };
        using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery(wql), options);
        var results = new List<ManagementBaseObject>();
        foreach (var obj in searcher.Get())
            results.Add(obj);
        return results;
    }

    /// <summary>
    /// Owner of a Win32_Process. Both are null when it can't be read: the process ended while
    /// listing, or it belongs to an account this user may not query.
    /// </summary>
    public static (string? User, string? Domain) GetOwner(ManagementObject process)
    {
        try
        {
            var args = new object[2];
            if (Convert.ToInt32(process.InvokeMethod("GetOwner", args)) == 0)
                return (args[0] as string, args[1] as string);
        }
        catch (Exception ex) when (ex is ManagementException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
        }
        return (null, null);
    }
}
