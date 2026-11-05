#Requires -RunAsAdministrator
<#
.SYNOPSIS
    SnoozeIt Automated Setup & Reinstallation Script
.DESCRIPTION
    Stops active processes/services, cleanly uninstalls previous SnoozeIt installations,
    trusts the Authenticode certificate, installs the new SnoozeIt MSI, and starts the service.
#>

param(
    [string]$MsiPath = "$PSScriptRoot\installer\SnoozeItInstaller.msi",
    [switch]$LaunchClient = $true
)

$ErrorActionPreference = "Stop"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "         SnoozeIt Setup & Fresh Reinstallation           " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host ""

# 1. Resolve MSI Path
if (-not (Test-Path $MsiPath)) {
    # Check alternate location if run from subfolder
    $altPath = Join-Path $PSScriptRoot "..\installer\SnoozeItInstaller.msi"
    if (Test-Path $altPath) {
        $MsiPath = (Resolve-Path $altPath).Path
    } else {
        Write-Error "MSI installer not found at '$MsiPath'. Please build or download it first."
        exit 1
    }
}
$MsiPath = (Resolve-Path $MsiPath).Path
Write-Host "[1/6] Using Installer: $MsiPath" -ForegroundColor Green

# 2. Stop running SnoozeIt Client applications
Write-Host "[2/6] Closing any running SnoozeIt Client applications..." -ForegroundColor Yellow
Get-Process -Name "SnoozeIt.Client" -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Host "      Stopping process PID $($_.Id)..." -ForegroundColor Gray
    Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
}

# 3. Stop running SnoozeIt Service
Write-Host "[3/6] Stopping SnoozeIt Service if running..." -ForegroundColor Yellow
$service = Get-Service -Name "SnoozeItService" -ErrorAction SilentlyContinue
if ($service) {
    if ($service.Status -ne "Stopped") {
        Write-Host "      Stopping service $($service.Name)..." -ForegroundColor Gray
        Stop-Service -Name "SnoozeItService" -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
    }
}

# 4. Uninstall any previous installation
Write-Host "[4/6] Checking for and removing previous SnoozeIt installations..." -ForegroundColor Yellow
$uninstallKeys = @(
    "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*",
    "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*"
)

$installedApps = Get-ItemProperty $uninstallKeys -ErrorAction SilentlyContinue | 
    Where-Object { $_.DisplayName -and $_.DisplayName -like "*SnoozeIt*" }

if ($installedApps) {
    foreach ($app in $installedApps) {
        Write-Host "      Found existing installation: '$($app.DisplayName)' ($($app.PSChildName))" -ForegroundColor Cyan
        $productCode = $app.PSChildName
        Write-Host "      Uninstalling previous version..." -ForegroundColor Gray
        $proc = Start-Process "msiexec.exe" -ArgumentList "/x $productCode /qn /norestart" -Wait -PassThru
        if ($proc.ExitCode -eq 0 -or $proc.ExitCode -eq 3010) {
            Write-Host "      Previous version successfully removed." -ForegroundColor Green
        } else {
            Write-Host "      Uninstall finished with code $($proc.ExitCode)." -ForegroundColor Yellow
        }
    }
} else {
    Write-Host "      No previous installation found in registry." -ForegroundColor Gray
}

# 5. Trust Authenticode Certificate
Write-Host "[5/6] Trusting self-signed code-signing certificate..." -ForegroundColor Yellow
try {
    $sig = Get-AuthenticodeSignature -FilePath $MsiPath
    if ($sig -and $sig.SignerCertificate) {
        $cert = $sig.SignerCertificate
        $store = New-Object System.Security.Cryptography.X509Certificates.X509Store "Root", "LocalMachine"
        $store.Open("ReadWrite")
        $store.Add($cert)
        $store.Close()
        Write-Host "      Certificate '$($cert.Subject)' trusted in LocalMachine\Root." -ForegroundColor Green
    }
} catch {
    Write-Host "      Note: Could not add certificate to store: $($_.Exception.Message)" -ForegroundColor DarkYellow
}

# 6. Install the new MSI
Write-Host "[6/6] Installing new SnoozeIt MSI..." -ForegroundColor Yellow
$installArgs = "/i `"$MsiPath`" /qn /norestart"
$installProc = Start-Process "msiexec.exe" -ArgumentList $installArgs -Wait -PassThru

if ($installProc.ExitCode -ne 0 -and $installProc.ExitCode -ne 3010) {
    Write-Error "MSI installation failed with exit code $($installProc.ExitCode)."
    exit $installProc.ExitCode
}

Write-Host "      Installation completed successfully!" -ForegroundColor Green
Start-Sleep -Seconds 2

# 7. Verify and start SnoozeIt Service
Write-Host ""
Write-Host "Verifying SnoozeIt Service status..." -ForegroundColor Cyan
$svc = Get-Service -Name "SnoozeItService" -ErrorAction SilentlyContinue
if ($svc) {
    if ($svc.Status -ne "Running") {
        Write-Host "Starting SnoozeItService..." -ForegroundColor Yellow
        Start-Service -Name "SnoozeItService" -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
        $svc.Refresh()
    }
    Write-Host "SnoozeItService Status: $($svc.Status)" -ForegroundColor Green
} else {
    Write-Host "Warning: SnoozeItService was not detected." -ForegroundColor Red
}

# 8. Launch Client UI
$clientPath = "C:\Program Files\SnoozeIt\SnoozeIt.Client.exe"
if ($LaunchClient -and (Test-Path $clientPath)) {
    Write-Host ""
    Write-Host "Launching SnoozeIt Client UI..." -ForegroundColor Cyan
    Start-Process -FilePath $clientPath -WorkingDirectory (Split-Path $clientPath)
    Write-Host "SnoozeIt is now running and protecting your workstation!" -ForegroundColor Green
} else {
    Write-Host ""
    Write-Host "Setup complete. You can launch SnoozeIt from the Start Menu or at '$clientPath'." -ForegroundColor Green
}

