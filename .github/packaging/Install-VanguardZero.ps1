# VANGUARD ZERO — Windows Installer & Launch Helper
# Installs VANGUARD ZERO to %LOCALAPPDATA%\VanguardZero, unblocks SmartScreen Zone.Identifier,
# and creates Desktop + Start Menu shortcuts.

param(
    [string]$InstallDir = "$env:LOCALAPPDATA\VanguardZero",
    [switch]$LaunchAfterInstall = $true
)

$ErrorActionPreference = "Stop"
$SourceDir = Split-Path -Parent $MyInvocation.MyCommand.Path

Write-Host "=================================================" -ForegroundColor Cyan
Write-Host "  VANGUARD ZERO — Tactical FPS Installer (x64)   " -ForegroundColor Cyan
Write-Host "=================================================" -ForegroundColor Cyan
Write-Host ""

if (-not (Test-Path (Join-Path $SourceDir "VanguardZero.exe"))) {
    Write-Error "VanguardZero.exe was not found next to this installer script. Extract the full ZIP archive first."
    exit 1
}

Write-Host "[1/3] Installing game files to $InstallDir ..." -ForegroundColor Gray
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Copy-Item -Path (Join-Path $SourceDir "*") -Destination $InstallDir -Recurse -Force

Write-Host "[2/3] Clearing Windows download Zone.Identifier flags ..." -ForegroundColor Gray
Get-ChildItem -Path $InstallDir -Recurse -File | ForEach-Object {
    try { Unblock-File -Path $_.FullName -ErrorAction SilentlyContinue } catch {}
}

Write-Host "[3/3] Creating Start Menu & Desktop shortcuts ..." -ForegroundColor Gray
$ExePath = Join-Path $InstallDir "VanguardZero.exe"
$WScriptShell = New-Object -ComObject WScript.Shell

$DesktopShortcut = $WScriptShell.CreateShortcut((Join-Path ([Environment]::GetFolderPath("Desktop")) "VANGUARD ZERO.lnk"))
$DesktopShortcut.TargetPath = $ExePath
$DesktopShortcut.WorkingDirectory = $InstallDir
$DesktopShortcut.Description = "Launch VANGUARD ZERO Multiplayer Tactical FPS"
$DesktopShortcut.Save()

$StartMenuDir = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\VANGUARD ZERO"
New-Item -ItemType Directory -Force -Path $StartMenuDir | Out-Null
$StartShortcut = $WScriptShell.CreateShortcut((Join-Path $StartMenuDir "VANGUARD ZERO.lnk"))
$StartShortcut.TargetPath = $ExePath
$StartShortcut.WorkingDirectory = $InstallDir
$StartShortcut.Description = "Launch VANGUARD ZERO Multiplayer Tactical FPS"
$StartShortcut.Save()

Write-Host ""
Write-Host "Installation complete! Installed to: $InstallDir" -ForegroundColor Green

if ($LaunchAfterInstall) {
    Write-Host "Launching VANGUARD ZERO..." -ForegroundColor Cyan
    Start-Process -FilePath $ExePath -WorkingDirectory $InstallDir
}
