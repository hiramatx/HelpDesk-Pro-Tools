using System;
using System.IO;
using System.Management;
using System.Security;
using Microsoft.Win32;

namespace HelpDesk_Pro_Tools.Services;

/// <summary>
/// Reads HKLM (64-bit view) on a remote PC. Uses the Remote Registry service when it can be reached
/// (a plain RPC per read, much cheaper than a WMI method call), otherwise falls back to WMI's
/// StdRegProv, which works without the service. Safe to share between PC Details sections running
/// in parallel: native reads need no lock, WMI calls are serialized.
/// </summary>
public sealed class RemoteRegistry : IDisposable
{
    private const uint HKLM = 0x80000002;
    private const string ProbeKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";

    private readonly RegistryKey? _native;
    private readonly ManagementClass? _wmi;
    private readonly object _wmiLock = new();

    public RemoteRegistry(string pc)
    {
        _native = LoadTrace.Step("open Remote Registry service", () => TryOpenNative(pc));
        if (_native is null)
            _wmi = new ManagementClass(WmiHelper.Connect(pc, @"root\default"), new ManagementPath("StdRegProv"), null);
    }

    /// <summary>Which route the reads go through, for the PC Details load-time tooltip.</summary>
    public string Source => _native is not null ? "Remote Registry service" : "WMI StdRegProv";

    /// <summary>
    /// HKLM through the Remote Registry service, or null if the service is disabled or unreachable.
    /// A probe read makes sure the connection actually works before we rely on it.
    /// </summary>
    private static RegistryKey? TryOpenNative(string pc)
    {
        RegistryKey? hklm = null;
        try
        {
            hklm = RegistryKey.OpenRemoteBaseKey(RegistryHive.LocalMachine, pc, RegistryView.Registry64);
            using var probe = hklm.OpenSubKey(ProbeKey);
            if (probe is not null) return hklm;
        }
        catch (Exception)
        {
            // Service disabled / stopped, blocked by the firewall or access denied: use WMI instead.
        }
        hklm?.Dispose();
        return null;
    }

    public string? GetString(string key, string valueName) =>
        LoadTrace.Count("registry reads", () => ReadString(key, valueName));

    private string? ReadString(string key, string valueName)
    {
        if (_native is not null)
            return ReadNative(key, valueName) as string;

        using var result = Invoke("GetStringValue", key, valueName);
        return result is null ? null : result["sValue"] as string;
    }

    public uint? GetDword(string key, string valueName) =>
        LoadTrace.Count("registry reads", () => ReadDword(key, valueName));

    private uint? ReadDword(string key, string valueName)
    {
        if (_native is not null)
            return ReadNative(key, valueName) is int v ? unchecked((uint)v) : null;

        using var result = Invoke("GetDWORDValue", key, valueName);
        return result?["uValue"] is { } wmiValue ? Convert.ToUInt32(wmiValue) : null;
    }

    /// <summary>Names of the subkeys under <paramref name="key"/> (empty if the key doesn't exist).</summary>
    public string[] GetSubKeyNames(string key) =>
        LoadTrace.Count("registry reads", () => ReadSubKeyNames(key));

    private string[] ReadSubKeyNames(string key)
    {
        if (_native is not null)
        {
            try
            {
                using var sub = _native.OpenSubKey(Normalize(key));
                return sub?.GetSubKeyNames() ?? Array.Empty<string>();
            }
            catch (Exception ex) when (IsReadError(ex))
            {
                return Array.Empty<string>();
            }
        }

        lock (_wmiLock) return EnumKey(key);
    }

    /// <summary>The raw value, or null when the key / value doesn't exist or can't be read (same as StdRegProv).</summary>
    private object? ReadNative(string key, string valueName)
    {
        try
        {
            using var sub = _native!.OpenSubKey(Normalize(key));
            // Don't expand REG_EXPAND_SZ here: it would use this PC's environment, not the remote one.
            return sub?.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        }
        catch (Exception ex) when (IsReadError(ex))
        {
            return null;
        }
    }

    private static bool IsReadError(Exception ex) =>
        ex is SecurityException or UnauthorizedAccessException or IOException;

    private string[] EnumKey(string key)
    {
        using var inParams = _wmi!.GetMethodParameters("EnumKey");
        inParams["hDefKey"] = HKLM;
        inParams["sSubKeyName"] = Normalize(key);

        using var outParams = _wmi.InvokeMethod("EnumKey", inParams, null);
        return Convert.ToUInt32(outParams["ReturnValue"]) == 0 && outParams["sNames"] is string[] names
            ? names
            : Array.Empty<string>();
    }

    // Accept keys pasted from regedit: "HKEY_LOCAL_MACHINE\SOFTWARE\..." or "HKLM\SOFTWARE\...".
    private static string Normalize(string key)
    {
        foreach (var prefix in new[] { @"HKEY_LOCAL_MACHINE\", @"HKLM\", @"HKLM:\", @"Computer\HKEY_LOCAL_MACHINE\" })
        {
            if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return key[prefix.Length..];
        }
        return key;
    }

    /// <summary>Returns the WMI out-parameters, or null when the key / value doesn't exist.</summary>
    private ManagementBaseObject? Invoke(string method, string key, string valueName)
    {
        lock (_wmiLock) return InvokeLocked(method, key, valueName);
    }

    private ManagementBaseObject? InvokeLocked(string method, string key, string valueName)
    {
        using var inParams = _wmi!.GetMethodParameters(method);
        inParams["hDefKey"] = HKLM;
        inParams["sSubKeyName"] = Normalize(key);
        inParams["sValueName"] = valueName;

        var outParams = _wmi.InvokeMethod(method, inParams, null);
        if (Convert.ToUInt32(outParams["ReturnValue"]) == 0) return outParams;

        outParams.Dispose();
        return null;
    }

    public void Dispose()
    {
        _native?.Dispose();
        _wmi?.Dispose();
    }
}
