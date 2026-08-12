[CmdletBinding()]
param(
    [string]$OutputPath,
    [switch]$RequireController,
    [switch]$RequirePhysicalHidden
)

$ErrorActionPreference = 'Stop'
$checks = [System.Collections.Generic.List[object]]::new()

function Add-Check([string]$Name, [bool]$Passed, [string]$Detail, [bool]$Required = $true) {
    $checks.Add([pscustomobject]@{ Name = $Name; Passed = $Passed; Required = $Required; Detail = $Detail })
}

$uninstallKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\AFGCPCManager'
$registration = Get-ItemProperty -LiteralPath $uninstallKey -ErrorAction SilentlyContinue
$installDirectory = if ($registration.InstallLocation) { [string]$registration.InstallLocation } else { Join-Path $env:ProgramFiles 'AFGC PC Manager' }
Add-Check 'Windows uninstall registration' ($null -ne $registration) $(if ($registration) { $uninstallKey } else { 'Registration not found.' })

foreach ($name in @('AFGCPCManager.exe', 'AFGCPCManager.HidVisibilityProbe.exe', 'AFGCPCManager.Setup.exe', 'AFGCPCManager.Uninstaller.exe', 'install-journal.json')) {
    $path = Join-Path $installDirectory $name
    Add-Check "Installed file: $name" (Test-Path -LiteralPath $path -PathType Leaf) $path
}

$pnpDevices = @()
try {
    $pnpDevices = @(Get-PnpDevice -PresentOnly -ErrorAction Stop)
    Add-Check 'PnP inventory query' $true 'Windows device inventory is readable.'
}
catch {
    Add-Check 'PnP inventory query' $false "Run this audit from an elevated PowerShell window. $($_.Exception.Message)"
}

$vjoy = $pnpDevices | Where-Object {
    $_.FriendlyName -match 'vJoy' -or $_.InstanceId -match 'ROOT\\VID_1234&PID_BEAD'
}
Add-Check 'vJoy device present' ($null -ne $vjoy) $(if ($vjoy) { ($vjoy.FriendlyName -join ', ') } else { 'No present vJoy device found.' })

$viGEmBus = $pnpDevices | Where-Object {
    $_.FriendlyName -match 'ViGEm|Virtual Gamepad Emulation Bus' -or
    $_.InstanceId -match 'ROOT\\VIGEMBUS'
}
Add-Check 'ViGEmBus device present' ($null -ne $viGEmBus) $(if ($viGEmBus) { ($viGEmBus.FriendlyName -join ', ') } else { 'No present ViGEmBus device found.' })

$hidHide = Get-Service -Name 'HidHide' -ErrorAction SilentlyContinue
Add-Check 'HidHide service present' ($null -ne $hidHide) $(if ($hidHide) { "Status: $($hidHide.Status)" } else { 'HidHide service not found.' })
Add-Check 'HidHide service running' ($null -ne $hidHide -and $hidHide.Status -eq 'Running') $(if ($hidHide) { "Status: $($hidHide.Status)" } else { 'HidHide service not found.' })

$controller = $pnpDevices | Where-Object {
    $_.InstanceId -match 'VID_1949&PID_0402|VID&00021949_PID&0402' -or $_.FriendlyName -eq 'Amazon Fire Game Controller'
}
Add-Check 'Amazon Fire Game Controller present' ($null -ne $controller) $(if ($controller) { ($controller.FriendlyName -join ', ') } else { 'Pair and wake the controller, then rerun.' }) ($RequireController.IsPresent -or $RequirePhysicalHidden.IsPresent)

if ($RequirePhysicalHidden) {
    $application = Get-Process -Name 'AFGCPCManager' -ErrorAction SilentlyContinue
    Add-Check 'AFGC PC Manager running for isolation test' ($null -ne $application) $(if ($application) { "Process count: $($application.Count)" } else { 'Start AFGC PC Manager, then rerun.' })
    $probe = Join-Path $installDirectory 'AFGCPCManager.HidVisibilityProbe.exe'
    if (Test-Path -LiteralPath $probe -PathType Leaf) {
        $probeOutput = & $probe --assert-none-visible 2>&1 | Out-String
        $probeExit = $LASTEXITCODE
        Add-Check 'Physical controller hidden from independent process' ($probeExit -eq 0) $(if ($probeOutput.Trim()) { $probeOutput.Trim() } else { "Probe exit code: $probeExit" })
    } else {
        Add-Check 'Physical controller hidden from independent process' $false 'Visibility probe is missing.'
    }
}

$signature = Get-AuthenticodeSignature -LiteralPath (Join-Path $installDirectory 'AFGCPCManager.exe') -ErrorAction SilentlyContinue
Add-Check 'Application publisher signature' ($signature.Status -eq 'Valid') $(if ($signature) { "Status: $($signature.Status)" } else { 'Application executable unavailable.' }) $false

$report = [pscustomobject]@{
    GeneratedAt = [DateTimeOffset]::Now
    Computer = $env:COMPUTERNAME
    InstallDirectory = $installDirectory
    Checks = $checks
}

$text = @(
    'AFGC PC Manager installed-system audit'
    "Generated: $($report.GeneratedAt)"
    "Install directory: $installDirectory"
    ''
    $checks | ForEach-Object { "[$(if ($_.Passed) { 'PASS' } elseif ($_.Required) { 'FAIL' } else { 'INFO' })] $($_.Name): $($_.Detail)" }
) -join [Environment]::NewLine
$text

if ($OutputPath) {
    $fullPath = [IO.Path]::GetFullPath($OutputPath)
    $parent = Split-Path -Parent $fullPath
    if ($parent) { New-Item -ItemType Directory -Force -Path $parent | Out-Null }
    $report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $fullPath -Encoding utf8
    Write-Host "Saved JSON report: $fullPath"
}

if ($checks.Where({ $_.Required -and -not $_.Passed }).Count -gt 0) { exit 1 }
