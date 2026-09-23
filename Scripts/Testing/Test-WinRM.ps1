param(
    [Parameter(Mandatory)]
    [string]$ComputerName
)

Write-Host "Testing WinRM on $ComputerName..." -ForegroundColor Cyan
try {
    Test-WSMan -ComputerName $ComputerName -ErrorAction Stop | Format-List
    Invoke-Command -ComputerName $ComputerName -ScriptBlock { "Remoting OK - running as $(whoami) on $env:COMPUTERNAME" }
}
catch {
    Write-Host "WinRM test failed: $($_.Exception.Message)" -ForegroundColor Red
}
