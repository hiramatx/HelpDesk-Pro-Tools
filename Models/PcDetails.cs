using System;
using System.Collections.Generic;

namespace HelpDesk_Pro_Tools.Models;

public class PcDetails
{
    // ---- Utilization gauges
    public double CpuPercent { get; set; }
    public double RamPercent { get; set; }
    public double RamUsedGb { get; set; }
    public double RamTotalGb { get; set; }
    public double DiskPercent { get; set; }
    public double DiskUsedGb { get; set; }
    public double DiskTotalGb { get; set; }

    // ---- PC
    public string ComputerName { get; set; } = "";
    public string Model { get; set; } = "";
    public string OperatingSystem { get; set; } = "";
    public string SerialNumber { get; set; } = "";
    public string Uac { get; set; } = "";
    public string DomainController { get; set; } = "";
    public string IpAddress { get; set; } = "";
    public string NetworkSpeed { get; set; } = "";
    public DateTime? LastBoot { get; set; }
    public string PcOu { get; set; } = "";

    // ---- Users
    public string LoggedInUser { get; set; } = "";
    public string LoggedInUserOu { get; set; } = "";
    public string LoggedInFrom { get; set; } = "";
    public string LocalAdmins { get; set; } = "";
    public string RemoteDesktopUsers { get; set; } = "";
    public string DirectAccessUsers { get; set; } = "";

    // ---- Hardware
    public string Processor { get; set; } = "";
    public string TotalRam { get; set; } = "";
    public string MacAddress { get; set; } = "";
    public string RamConfig { get; set; } = "";
    public List<string> VideoCards { get; } = new();
    public List<MonitorInfo> Monitors { get; } = new();
    public List<DriveSpace> Drives { get; } = new();

    // ---- Software
    public string EdgeVersion { get; set; } = "";
    public string ChromeVersion { get; set; } = "";
    public string OfficeVersion { get; set; } = "";

    public List<DeviceError> DeviceErrors { get; } = new();

    /// <summary>Non-fatal problems hit while collecting data (one per failed section).</summary>
    public List<string> Warnings { get; } = new();
}

public record MonitorInfo(string Name, string Resolution);

public record DriveSpace(string Letter, double TotalGb, double UsedGb, double FreeGb);

public record DeviceError(string Name, int Code, string Description);

/// <summary>One "Label: value" cell in a PC Details section. An empty label is a spacer that keeps rows aligned.</summary>
public record InfoField(string Label, string Value)
{
    public bool IsSpacer => Label.Length == 0;

    public static InfoField Spacer { get; } = new("", "");
}

/// <summary>One row of a 2-column section. Each row is only as tall as its own content.</summary>
public record InfoRow(InfoField Left, InfoField Right);
