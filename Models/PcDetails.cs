using System;
using System.Collections.Generic;

namespace HelpDesk_Pro_Tools.Models;

public class PcDetails
{
    public string PcName { get; set; } = "";
    public string PcOu { get; set; } = "";
    public string OperatingSystem { get; set; } = "";
    public DateTime? LastBoot { get; set; }

    public double CpuPercent { get; set; }
    public double RamPercent { get; set; }
    public double RamUsedGb { get; set; }
    public double RamTotalGb { get; set; }
    public double DiskPercent { get; set; }
    public double DiskUsedGb { get; set; }
    public double DiskTotalGb { get; set; }

    public string LoggedInUser { get; set; } = "";
    public string LoggedInUserOu { get; set; } = "";

    public List<VideoCardInfo> VideoCards { get; } = new();
    public List<MonitorInfo> Monitors { get; } = new();
    public List<DeviceError> DeviceErrors { get; } = new();

    /// <summary>Non-fatal problems hit while collecting data (one per failed section).</summary>
    public List<string> Warnings { get; } = new();
}

public record VideoCardInfo(string Name, string Driver, string Resolution);

public record MonitorInfo(string Name, string Resolution);

public record DeviceError(string Name, int Code, string Description);
