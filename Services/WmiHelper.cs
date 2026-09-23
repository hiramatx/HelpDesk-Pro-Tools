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
}
