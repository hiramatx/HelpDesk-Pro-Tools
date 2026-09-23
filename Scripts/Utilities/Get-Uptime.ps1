param(
    [Parameter(Mandatory)]
    [string]$ComputerName
)

$os = Get-CimInstance -ClassName Win32_OperatingSystem -ComputerName $ComputerName
$uptime = (Get-Date) - $os.LastBootUpTime

[pscustomobject]@{
    Computer = $ComputerName
    LastBoot = $os.LastBootUpTime
    Uptime   = '{0}d {1}h {2}m' -f $uptime.Days, $uptime.Hours, $uptime.Minutes
} | Format-List
