#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Removes the Extensor.WindowsUpdatesMonitor Windows service.
.DESCRIPTION
    Stops and deletes the service and its Event Log source. The folder with the exe (and these scripts) is left
    alone; delete it by hand if it is no longer wanted.
.PARAMETER RemoveData
    Also delete %ProgramData%\Extensor.WindowsUpdatesMonitor (state and logs).
#>
param(
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

$dataDir = Join-Path $env:ProgramData 'Extensor.WindowsUpdatesMonitor'
if ($RemoveData -and (Test-Path $dataDir)) {
    Write-Host "Removing $dataDir"
    Remove-Item $dataDir -Recurse -Force
}

Write-Host "Uninstalled."
