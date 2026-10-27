#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Installs the UpdateNag Windows service.
.PARAMETER SourceDir
    Folder containing the published UpdateNag.exe and appsettings.json (default: .\publish).
.PARAMETER InstallDir
    Where to copy the files (default: C:\Program Files\UpdateNag).
#>
param(
    [string]$SourceDir = (Join-Path $PSScriptRoot 'publish'),
    [string]$InstallDir = (Join-Path $env:ProgramFiles 'UpdateNag')
)

$ErrorActionPreference = 'Stop'
$serviceName = 'UpdateNag'
$exe = Join-Path $InstallDir 'UpdateNag.exe'

if (-not (Test-Path (Join-Path $SourceDir 'UpdateNag.exe'))) {
    throw "UpdateNag.exe not found in '$SourceDir'. Run: dotnet publish src/UpdateNag -c Release -o publish"
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
Copy-Item (Join-Path $SourceDir 'UpdateNag.exe') $InstallDir -Force
# Keep a locally edited appsettings.json on upgrade.
if (-not (Test-Path (Join-Path $InstallDir 'appsettings.json'))) {
    Copy-Item (Join-Path $SourceDir 'appsettings.json') $InstallDir
}

if (-not [System.Diagnostics.EventLog]::SourceExists($serviceName)) {
    Write-Host "Registering Event Log source '$serviceName'"
    [System.Diagnostics.EventLog]::CreateEventSource($serviceName, "Application")
}

if (-not $existing) {
    Write-Host "Creating service"
    sc.exe create $serviceName binPath= "`"$exe`"" start= delayed-auto obj= LocalSystem DisplayName= "Windows Update Nag" | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "sc.exe create failed ($LASTEXITCODE)" }
    sc.exe description $serviceName "Reminds logged-in users once a day when Windows Updates or a restart are pending." | Out-Null
    # Restart after a crash: 1 min, 1 min, then 5 min; reset the counter after a day.
    sc.exe failure $serviceName reset= 86400 actions= restart/60000/restart/60000/restart/300000 | Out-Null
}
else {
    sc.exe config $serviceName binPath= "`"$exe`"" start= delayed-auto | Out-Null
}

Start-Service -Name $serviceName
Get-Service -Name $serviceName | Format-Table Name, Status, StartType -AutoSize
Write-Host "Installed. Logs: $env:ProgramData\UpdateNag\logs"
