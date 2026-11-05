#Requires -RunAsAdministrator
<#
.SYNOPSIS
    SnoozeIt Demo Script — launches sample workloads, registers & starts the service,
    then opens the SnoozeIt client so you can see the recommendation engine in action.

.DESCRIPTION
    1. Launches three "heavy" background processes (Notepad instances + a synthetic memory hog).
    2. Registers SnoozeIt.Service as a Windows Service (if not already registered).
    3. Starts the service.
    4. Launches the SnoozeIt.Client UI.
    5. Waits 60 seconds for you to observe the scoring engine, then cleans up.

.NOTES
    Run this script from an elevated (Administrator) PowerShell prompt.
    Assumes the solution has been built in Release configuration.
#>

param(
    [string]$Configuration = "Release",
    [string]$RepoRoot = $PSScriptRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ── Paths ──────────────────────────────────────────────────────────────────────
$serviceBin  = Join-Path $RepoRoot "SnoozeIt.Service\bin\$Configuration\net10.0\SnoozeIt.Service.exe"
$clientBin   = Join-Path $RepoRoot "SnoozeIt.Client\bin\$Configuration\net10.0-windows10.0.19041.0\SnoozeIt.Client.exe"
$serviceName = "SnoozeItService"

# ── 1. Launch sample workloads ─────────────────────────────────────────────────
Write-Host "`n🚀  Launching sample workloads..." -ForegroundColor Cyan

# Three Notepad instances (lightweight, easy to snooze)
$notepadPids = @()
for ($i = 1; $i -le 3; $i++) {
    $p = Start-Process notepad.exe -PassThru
    $notepadPids += $p.Id
    Write-Host "   ✅  Notepad #$i launched (PID $($p.Id))"
}

# Synthetic memory-hog script (allocates ~200 MB then idles)
$hogScript = @'
$list = [System.Collections.Generic.List[byte[]]]::new()
for ($i = 0; $i -lt 200; $i++) {
    $buf = [byte[]]::new(1MB)
    [System.Security.Cryptography.RandomNumberGenerator]::Fill($buf)
    $list.Add($buf)
}
Write-Host "Memory hog: allocated ~200 MB. Idling..."
Start-Sleep -Seconds 300
'@
$hogProc = Start-Process powershell.exe -ArgumentList "-NoProfile","-Command",$hogScript -PassThru
Write-Host "   ✅  Memory-hog process launched (PID $($hogProc.Id))"

# ── 2. Register the service ────────────────────────────────────────────────────
Write-Host "`n🔧  Checking SnoozeIt service registration..." -ForegroundColor Cyan

$svc = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if (-not $svc) {
    if (Test-Path $serviceBin) {
        Write-Host "   Registering service: $serviceBin"
        sc.exe create $serviceName binPath= "`"$serviceBin`"" start= auto | Out-Null
        Write-Host "   ✅  Service registered."
    } else {
        Write-Host "   ⚠️  Service binary not found at: $serviceBin" -ForegroundColor Yellow
        Write-Host "       Build the solution first:  dotnet build SnoozeIt.slnx -c $Configuration" -ForegroundColor Yellow
    }
} else {
    Write-Host "   ✅  Service already registered."
}

# ── 3. Start the service ───────────────────────────────────────────────────────
Write-Host "`n▶️   Starting SnoozeIt service..." -ForegroundColor Cyan
$svc = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($svc -and $svc.Status -ne 'Running') {
    Start-Service -Name $serviceName
    Write-Host "   ✅  Service started."
} elseif ($svc) {
    Write-Host "   ✅  Service is already running."
} else {
    Write-Host "   ⚠️  Service not found; skipping start." -ForegroundColor Yellow
}

# ── 4. Launch the client UI ───────────────────────────────────────────────────
Write-Host "`n🖥️   Launching SnoozeIt client..." -ForegroundColor Cyan
if (Test-Path $clientBin) {
    Start-Process $clientBin
    Write-Host "   ✅  Client launched."
} else {
    Write-Host "   ⚠️  Client binary not found at: $clientBin" -ForegroundColor Yellow
    Write-Host "       Build the solution first:  dotnet build SnoozeIt.slnx -c $Configuration" -ForegroundColor Yellow
}

# ── 5. Demo observation window ────────────────────────────────────────────────
Write-Host "`n⏱️   Demo running — observe the SnoozeIt UI for 60 seconds..." -ForegroundColor Green
Write-Host "   The scoring engine should recommend snoozing the Notepad instances" -ForegroundColor Green
Write-Host "   and the memory-hog process within ~30-40 seconds." -ForegroundColor Green
Write-Host ""
Start-Sleep -Seconds 60

# ── 6. Cleanup ────────────────────────────────────────────────────────────────
Write-Host "`n🧹  Cleaning up demo workloads..." -ForegroundColor Cyan
foreach ($pid in $notepadPids) {
    Stop-Process -Id $pid -Force -ErrorAction SilentlyContinue
}
Stop-Process -Id $hogProc.Id -Force -ErrorAction SilentlyContinue
Write-Host "   ✅  Sample workloads terminated."
Write-Host "`n🎉  Demo complete!" -ForegroundColor Green

