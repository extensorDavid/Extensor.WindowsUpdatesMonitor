#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Removes the Extensor.WindowsUpdatesMonitor Windows service.
.PARAMETER InstallDir
    Install folder to delete (default: C:\Program Files\Extensor.WindowsUpdatesMonitor).
.PARAMETER RemoveData
    Also delete %ProgramData%\Extensor.WindowsUpdatesMonitor (state and logs).
#>
param(
    [string]$InstallDir = (Join-Path $env:ProgramFiles 'Extensor.WindowsUpdatesMonitor'),
    [switch]$RemoveData
)

$ErrorActionPreference = 'Stop'
$serviceName = 'Extensor.WindowsUpdatesMonitor'

$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($service) {
    if ($service.Status -ne 'Stopped') {
        Write-Host "Stopping service..."
        Stop-Service -Name $serviceName -Force
        $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
    }
    Write-Host "Deleting service"
    sc.exe delete $serviceName | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "sc.exe delete failed ($LASTEXITCODE)" }
}
else {
    Write-Host "Service '$serviceName' is not installed."
}

if ([System.Diagnostics.EventLog]::SourceExists($serviceName)) {
    Write-Host "Removing Event Log source '$serviceName'"
    [System.Diagnostics.EventLog]::DeleteEventSource($serviceName)
}

if (Test-Path $InstallDir) {
    Write-Host "Removing $InstallDir"
    Remove-Item $InstallDir -Recurse -Force
}

$dataDir = Join-Path $env:ProgramData 'Extensor.WindowsUpdatesMonitor'
if ($RemoveData -and (Test-Path $dataDir)) {
    Write-Host "Removing $dataDir"
    Remove-Item $dataDir -Recurse -Force
}

Write-Host "Uninstalled."
