using System;
using System.Management;

namespace HelpDesk_Pro_Tools.Services;

/// <summary>
/// Reads HKLM on a remote PC through WMI's StdRegProv, so it works without the
/// Remote Registry service being started.
/// </summary>
public sealed class RemoteRegistry : IDisposable
{
    private const uint HKLM = 0x80000002;
    private readonly ManagementClass _reg;

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

    /// <summary>Returns the out-parameters, or null when the key / value doesn't exist.</summary>
    private ManagementBaseObject? Invoke(string method, string key, string valueName)
    {
        using var inParams = _reg.GetMethodParameters(method);
        inParams["hDefKey"] = HKLM;
        inParams["sSubKeyName"] = key;
        inParams["sValueName"] = valueName;

        var outParams = _reg.InvokeMethod(method, inParams, null);
        if (Convert.ToUInt32(outParams["ReturnValue"]) == 0) return outParams;

        outParams.Dispose();
        return null;
    }

    public void Dispose() => _reg.Dispose();
}
