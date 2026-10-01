$ErrorActionPreference = 'Stop'
$InstallerPath = Join-Path $PSScriptRoot '..\Server\wwwroot\Content\Install-Remotely.ps1'
$Content = Get-Content -Path $InstallerPath -Raw
$Failures = @()

if ($Content -notmatch '-StartupType\s+Manual') {
    $Failures += 'Resident Agent service must be installed with StartupType Manual.'
}
if ($Content -match '-StartupType\s+Automatic') {
    $Failures += 'Resident Agent service must not be installed with StartupType Automatic.'
}
if ($Content -match 'Start-Service\s+-Name\s+Remotely_Service') {
    $Failures += 'Install-Remotely must leave Remotely_Service stopped for on-demand Manager control.'
}
if ($Content -notmatch 'Where-Object\s*\{\s*\$_\.Name\s+-notlike\s+"ConnectionInfo\.json"\s*\}') {
    $Failures += 'Installer update flow must preserve ConnectionInfo.json.'
}

if ($Failures.Count -gt 0) {
    $Failures | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Host 'Installer semantics validated: Manual, stopped, ConnectionInfo preserved.'
