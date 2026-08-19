# Απεγκατάσταση του Mars Terraforming (ανά χρήστη). Ζει μέσα στον φάκελο εγκατάστασης.
# Τα saves στο %APPDATA%\<AppName> ΔΕΝ πειράζονται.
[CmdletBinding()]
param([switch]$Silent)

$ErrorActionPreference = 'Stop'

$AppName = '@APP_NAME@'
$AppId   = '@APP_ID@'
$Dir     = Split-Path -Parent $MyInvocation.MyCommand.Path

Write-Host ""
Write-Host "$AppName - uninstall" -ForegroundColor Cyan
Write-Host "Removing: $Dir"

if (-not $Silent) {
    $answer = Read-Host "Continue? [y/N]"
    if ($answer -notmatch '^[YyΝν]') { Write-Host "Cancelled."; exit 2 }
}

foreach ($link in @(
    (Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\$AppName.lnk"),
    (Join-Path ([Environment]::GetFolderPath('Desktop')) "$AppName.lnk"))) {
    if (Test-Path $link) { Remove-Item $link -Force -ErrorAction SilentlyContinue }
}

$regKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\$AppId"
if (Test-Path $regKey) { Remove-Item $regKey -Recurse -Force -ErrorAction SilentlyContinue }

# Ο φάκελος διαγράφεται από «έξω», αφού μέσα του τρέχει αυτό το ίδιο script.
$cmd = "Start-Sleep -Milliseconds 700; Remove-Item -LiteralPath '$Dir' -Recurse -Force -ErrorAction SilentlyContinue"
Start-Process -FilePath 'powershell.exe' -WindowStyle Hidden `
    -ArgumentList '-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', $cmd

Write-Host ""
Write-Host "$AppName removed. Your saved games in %APPDATA%\$AppName were kept." -ForegroundColor Green
if (-not $Silent) { Start-Sleep -Seconds 2 }
exit 0
