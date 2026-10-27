#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Installs Extensor.WindowsUpdatesMonitor.exe as the Extensor.WindowsUpdatesMonitor Windows service and starts it.
.DESCRIPTION
    Put this script next to Extensor.WindowsUpdatesMonitor.exe (e.g. in
    C:\Program Files\Extensor\Extensor.WindowsUpdatesMonitor) and run it from an elevated PowerShell prompt. The
    service runs the exe in this script's folder; nothing is copied. Running it again updates an existing install.
#>

$ErrorActionPreference = 'Stop'
$serviceName = 'Extensor.WindowsUpdatesMonitor'
$exe = Join-Path $PSScriptRoot 'Extensor.WindowsUpdatesMonitor.exe'

if (-not (Test-Path $exe)) {
    throw "$exe not found. Put install.ps1 in the same folder as the exe."
}

# Stop an existing install (it may be running from another folder).
$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($existing -and $existing.Status -ne 'Stopped') {
    Write-Host "Stopping existing service..."
    Stop-Service -Name $serviceName -Force
    $existing.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
}

if (-not [System.Diagnostics.EventLog]::SourceExists($serviceName)) {
    Write-Host "Registering Event Log source '$serviceName'"
    [System.Diagnostics.EventLog]::CreateEventSource($serviceName, "Application")
}

if (-not $existing) {
    Write-Host "Creating service '$serviceName'"
    sc.exe create $serviceName binPath= "`"$exe`"" start= delayed-auto obj= LocalSystem DisplayName= "Extensor Windows Updates Monitor" | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "sc.exe create failed ($LASTEXITCODE)" }
}
else {
    sc.exe config $serviceName binPath= "`"$exe`"" start= delayed-auto obj= LocalSystem DisplayName= "Extensor Windows Updates Monitor" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "sc.exe config failed ($LASTEXITCODE)" }
}
sc.exe description $serviceName "Checks for pending Windows Updates every 2 hours, reports to the Extensor intranet and reminds logged-in users." | Out-Null
# Restart after a crash: 1 min, 1 min, then 5 min; reset the counter after a day.
sc.exe failure $serviceName reset= 86400 actions= restart/60000/restart/60000/restart/300000 | Out-Null

Write-Host "Starting service..."
Start-Service -Name $serviceName
# Make sure it stays up, rather than crashing straight after start.
Start-Sleep -Seconds 5
$service = Get-Service -Name $serviceName
$service | Format-Table Name, DisplayName, Status, StartType -AutoSize
if ($service.Status -ne 'Running') {
    throw "The service is '$($service.Status)', not running. See the Application event log (source $serviceName) and $env:ProgramData\Extensor.WindowsUpdatesMonitor\logs."
}
Write-Host "Installed and running. The first check runs within 10 seconds."
Write-Host "Logs: $env:ProgramData\Extensor.WindowsUpdatesMonitor\logs"
