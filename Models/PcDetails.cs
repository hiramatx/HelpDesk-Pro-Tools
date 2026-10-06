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
    public GroupMembers LocalAdmins { get; set; } = GroupMembers.Empty;
    public GroupMembers RemoteDesktopUsers { get; set; } = GroupMembers.Empty;
    public GroupMembers DirectAccessUsers { get; set; } = GroupMembers.Empty;

    // ---- Hardware
    public string Processor { get; set; } = "";
    public string TotalRam { get; set; } = "";
    public string MacAddress { get; set; } = "";
    public string RamConfig { get; set; } = "";
    public List<string> VideoCards { get; } = new();
    public List<MonitorInfo> Monitors { get; } = new();
    public List<DriveSpace> Drives { get; } = new();

    // ---- Software (from Config\software.json)
    public List<SoftwareResult> Software { get; } = new();

    /// <summary>When Group Policy last finished applying for the computer / the logged-in user (local time).</summary>
    public GpoDate GpoSystem { get; set; } = GpoDate.Unknown("Not read");
    public GpoDate GpoUser { get; set; } = GpoDate.Unknown("Not read");

    public List<DeviceError> DeviceErrors { get; } = new();

    /// <summary>Non-fatal problems hit while collecting data (one per failed section).</summary>
    public List<string> Warnings { get; } = new();
}

/// <summary>A Group Policy refresh time, or the reason there isn't one.</summary>
public record GpoDate(DateTime? When, string? Problem)
{
    public static GpoDate Unknown(string problem) => new(null, problem);
}

public record MonitorInfo(string Name, string Resolution);

public record DriveSpace(string Letter, double TotalGb, double UsedGb, double FreeGb);

public record DeviceError(string Name, int Code, string Description);

/// <summary>A group's member list as shown, and whether it actually lists members (not "(none)" or an error).</summary>
public record GroupMembers(string Text, bool HasMembers)
{
    public static GroupMembers Empty { get; } = new("", false);
}

/// <summary>How a value is coloured: Ok = green, Caution = yellow, Warn = orange, Bad = red, Normal = default text.</summary>
public enum FieldStatus { Normal, Ok, Bad, Warn, Caution }

/// <summary>One software line: the label shown, what to show, its colour and an optional hover explanation.</summary>
public record SoftwareResult(string Label, string Value, FieldStatus Status, string? ToolTip);

/// <summary>One "Label: value" cell in a PC Details section. An empty label is a spacer that keeps rows aligned.</summary>
public record InfoField(string Label, string Value)
{
    public FieldStatus Status { get; init; }
    public string? ToolTip { get; init; }

    public bool IsSpacer => Label.Length == 0;
    public bool IsOk => Status == FieldStatus.Ok;
    public bool IsBad => Status == FieldStatus.Bad;
    public bool IsWarn => Status == FieldStatus.Warn;
    public bool IsCaution => Status == FieldStatus.Caution;

    public static InfoField Spacer { get; } = new("", "");
}

/// <summary>One row of a 2-column section. Each row is only as tall as its own content.</summary>
public record InfoRow(InfoField Left, InfoField Right);
