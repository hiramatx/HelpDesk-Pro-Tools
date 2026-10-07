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
        LoadTrace.Step($@"WMI connect {wmiNamespace}", scope.Connect);
        return scope;
    }

    public static List<ManagementBaseObject> Query(ManagementScope scope, string wql) =>
        LoadTrace.Step($"WMI query {Describe(wql)}", () => Run(scope, wql));

    private static List<ManagementBaseObject> Run(ManagementScope scope, string wql)
    {
        var options = new EnumerationOptions { Timeout = TimeSpan.FromSeconds(30), ReturnImmediately = false };
        using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery(wql), options);
        var results = new List<ManagementBaseObject>();
        foreach (var obj in searcher.Get())
            results.Add(obj);
        return results;
    }

    // "SELECT Name FROM Win32_BIOS WHERE ..." -> "Win32_BIOS WHERE ..." (the class is what the log needs).
    private static string Describe(string wql)
    {
        var from = wql.IndexOf(" FROM ", StringComparison.OrdinalIgnoreCase);
        return from < 0 ? wql : wql[(from + 6)..];
    }
}
