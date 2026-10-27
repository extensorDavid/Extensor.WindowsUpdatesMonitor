#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Installs Extensor.WindowsUpdatesNagger.exe as the UpdateNag Windows service and starts it.
.DESCRIPTION
    Put this script next to Extensor.WindowsUpdatesNagger.exe (appsettings.json is optional) and run it from an
    elevated PowerShell prompt. Running it again upgrades an existing install.
.PARAMETER SourceDir
    Folder containing Extensor.WindowsUpdatesNagger.exe (default: this script's folder, else .\publish under it).
.PARAMETER InstallDir
    Where to copy the files (default: C:\Program Files\Extensor.WindowsUpdatesNagger).
#>
param(
    [string]$SourceDir,
    [string]$InstallDir = (Join-Path $env:ProgramFiles 'Extensor.WindowsUpdatesNagger')
)

$ErrorActionPreference = 'Stop'
$serviceName = 'UpdateNag'
$exeName = 'Extensor.WindowsUpdatesNagger.exe'
$exe = Join-Path $InstallDir $exeName

if (-not $SourceDir) {
    $SourceDir = $PSScriptRoot
    if (-not (Test-Path (Join-Path $SourceDir $exeName))) { $SourceDir = Join-Path $PSScriptRoot 'publish' }
}
if (-not (Test-Path (Join-Path $SourceDir $exeName))) {
    throw "$exeName not found next to this script or in .\publish. Put the exe next to install.ps1, or give -SourceDir."
}

# Stop an existing install so the exe can be overwritten.
$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($existing -and $existing.Status -ne 'Stopped') {
    Write-Host "Stopping existing service..."
    Stop-Service -Name $serviceName -Force
    $existing.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
}

Write-Host "Copying files to $InstallDir"
New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
Copy-Item (Join-Path $SourceDir $exeName) $InstallDir -Force
# appsettings.json is optional (the defaults are built in). Keep a locally edited one on upgrade.
$settings = Join-Path $SourceDir 'appsettings.json'
if ((Test-Path $settings) -and -not (Test-Path (Join-Path $InstallDir 'appsettings.json'))) {
    Copy-Item $settings $InstallDir
}

if (-not [System.Diagnostics.EventLog]::SourceExists($serviceName)) {
    Write-Host "Registering Event Log source '$serviceName'"
    [System.Diagnostics.EventLog]::CreateEventSource($serviceName, "Application")
}

if (-not $existing) {
    Write-Host "Creating service '$serviceName'"
    sc.exe create $serviceName binPath= "`"$exe`"" start= delayed-auto obj= LocalSystem DisplayName= "Extensor Windows Updates Nagger" | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "sc.exe create failed ($LASTEXITCODE)" }
}
else {
    sc.exe config $serviceName binPath= "`"$exe`"" start= delayed-auto obj= LocalSystem DisplayName= "Extensor Windows Updates Nagger" | Out-Null
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
    throw "The service is '$($service.Status)', not running. See the Application event log (source $serviceName) and $env:ProgramData\UpdateNag\logs."
}
Write-Host "Installed and running. The first check runs in about 5 minutes."
Write-Host "Logs: $env:ProgramData\UpdateNag\logs"
