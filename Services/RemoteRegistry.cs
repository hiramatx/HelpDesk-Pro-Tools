using System;
using System.Management;

namespace HelpDesk_Pro_Tools.Services;

/// <summary>
/// Reads HKLM on a remote PC through WMI's StdRegProv, so it works without the
/// Remote Registry service being started. Calls are serialized so PC Details sections running
/// in parallel can share one instance.
/// </summary>
public sealed class RemoteRegistry : IDisposable
{
    private const uint HKLM = 0x80000002;
    private readonly ManagementClass _reg;
    private readonly object _lock = new();

    public RemoteRegistry(string pc)
    {
        _reg = new ManagementClass(WmiHelper.Connect(pc, @"root\default"), new ManagementPath("StdRegProv"), null);
    }

    public string? GetString(string key, string valueName)
    {
        using var result = Invoke("GetStringValue", key, valueName);
        return result is null ? null : result["sValue"] as string;
    }

    public uint? GetDword(string key, string valueName)
    {
        using var result = Invoke("GetDWORDValue", key, valueName);
        return result?["uValue"] is { } v ? Convert.ToUInt32(v) : null;
    }

    /// <summary>Names of the subkeys under <paramref name="key"/> (empty if the key doesn't exist).</summary>
    public string[] GetSubKeyNames(string key)
    {
        lock (_lock) return EnumKey(key);
    }

    private string[] EnumKey(string key)
    {
        using var inParams = _reg.GetMethodParameters("EnumKey");
        inParams["hDefKey"] = HKLM;
        inParams["sSubKeyName"] = Normalize(key);

        using var outParams = _reg.InvokeMethod("EnumKey", inParams, null);
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

    /// <summary>Returns the out-parameters, or null when the key / value doesn't exist.</summary>
    private ManagementBaseObject? Invoke(string method, string key, string valueName)
    {
        lock (_lock) return InvokeLocked(method, key, valueName);
    }

    private ManagementBaseObject? InvokeLocked(string method, string key, string valueName)
    {
        using var inParams = _reg.GetMethodParameters(method);
        inParams["hDefKey"] = HKLM;
        inParams["sSubKeyName"] = Normalize(key);
        inParams["sValueName"] = valueName;

        var outParams = _reg.InvokeMethod(method, inParams, null);
        if (Convert.ToUInt32(outParams["ReturnValue"]) == 0) return outParams;

        outParams.Dispose();
        return null;
    }

    public void Dispose() => _reg.Dispose();
}
