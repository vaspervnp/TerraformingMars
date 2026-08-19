# Εγκατάσταση του Mars Terraforming στα Windows (ανά χρήστη — δεν χρειάζεται δικαιώματα διαχειριστή).
# Το τρέχει το install.cmd μέσα από το self-extracting setup· δεν προορίζεται για χειροκίνητη χρήση.
[CmdletBinding()]
param(
    [string]$Dir,            # πού θα εγκατασταθεί (default: %LOCALAPPDATA%\Programs\<AppName>)
    [switch]$Silent,         # χωρίς ερωτήσεις / χωρίς παύση στο τέλος
    [switch]$NoDesktopIcon,  # μόνο συντόμευση στο μενού Έναρξη
    [switch]$NoStartMenu
)

$ErrorActionPreference = 'Stop'

$AppName    = '@APP_NAME@'
$AppVersion = '@APP_VERSION@'
$ExeName    = '@EXE_NAME@'
$AppId      = '@APP_ID@'

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($Dir)) {
    $Dir = Join-Path $env:LOCALAPPDATA "Programs\$AppName"
}

function Write-Step([string]$text) { Write-Host "  $text" }

Write-Host ""
Write-Host "$AppName $AppVersion - setup" -ForegroundColor Cyan
Write-Host ("-" * 50)
Write-Host "Installing to: $Dir"

if (-not $Silent -and (Test-Path (Join-Path $Dir $ExeName))) {
    Write-Host ""
    Write-Host "A copy is already installed there and will be overwritten." -ForegroundColor Yellow
    $answer = Read-Host "Continue? [Y/n]"
    if ($answer -and $answer -notmatch '^[YyΝν]') { Write-Host "Cancelled."; exit 2 }
}

# --- αρχεία ---------------------------------------------------------------
Write-Step "unpacking files..."
New-Item -ItemType Directory -Force -Path $Dir | Out-Null

# Ένα σκέτο Expand-Archive δεν αντικαθιστά υπάρχοντα αρχεία, οπότε ξεπακετάρουμε «με το χέρι».
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zipPath = Join-Path $here 'payload.zip'
$zip = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    foreach ($entry in $zip.Entries) {
        $target = Join-Path $Dir $entry.FullName
        if ([string]::IsNullOrEmpty($entry.Name)) {          # καταχώρηση φακέλου
            New-Item -ItemType Directory -Force -Path $target | Out-Null
            continue
        }
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
        [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $true)
    }
}
finally { $zip.Dispose() }

$exePath = Join-Path $Dir $ExeName
if (-not (Test-Path $exePath)) { throw "Setup is broken: $ExeName not found after unpacking." }

# --- συντομεύσεις ---------------------------------------------------------
$shell = New-Object -ComObject WScript.Shell

function New-Shortcut([string]$linkPath, [string]$description) {
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $linkPath) | Out-Null
    $sc = $shell.CreateShortcut($linkPath)
    $sc.TargetPath       = $exePath
    $sc.WorkingDirectory = $Dir
    $sc.IconLocation     = "$exePath,0"
    $sc.Description      = $description
    $sc.Save()
}

if (-not $NoStartMenu) {
    Write-Step "Start menu shortcut..."
    New-Shortcut (Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\$AppName.lnk") "$AppName $AppVersion"
}
if (-not $NoDesktopIcon) {
    Write-Step "desktop shortcut..."
    New-Shortcut (Join-Path ([Environment]::GetFolderPath('Desktop')) "$AppName.lnk") "$AppName $AppVersion"
}

# --- απεγκατάσταση (Εφαρμογές & δυνατότητες) ------------------------------
Write-Step "registering uninstaller..."
$uninstallPs1 = Join-Path $Dir 'uninstall.ps1'
$regKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\$AppId"
New-Item -Path $regKey -Force | Out-Null
$size = [int]((Get-ChildItem $Dir -Recurse -File | Measure-Object -Property Length -Sum).Sum / 1KB)
$uninstallCmd = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$uninstallPs1`""
New-ItemProperty -Path $regKey -Name 'DisplayName'     -Value $AppName      -PropertyType String -Force | Out-Null
New-ItemProperty -Path $regKey -Name 'DisplayVersion'  -Value $AppVersion   -PropertyType String -Force | Out-Null
New-ItemProperty -Path $regKey -Name 'Publisher'       -Value '@PUBLISHER@' -PropertyType String -Force | Out-Null
New-ItemProperty -Path $regKey -Name 'DisplayIcon'     -Value $exePath      -PropertyType String -Force | Out-Null
New-ItemProperty -Path $regKey -Name 'InstallLocation' -Value $Dir          -PropertyType String -Force | Out-Null
New-ItemProperty -Path $regKey -Name 'UninstallString' -Value $uninstallCmd -PropertyType String -Force | Out-Null
New-ItemProperty -Path $regKey -Name 'EstimatedSize'   -Value $size         -PropertyType DWord  -Force | Out-Null
New-ItemProperty -Path $regKey -Name 'NoModify'        -Value 1             -PropertyType DWord  -Force | Out-Null
New-ItemProperty -Path $regKey -Name 'NoRepair'        -Value 1             -PropertyType DWord  -Force | Out-Null

Write-Host ""
Write-Host "$AppName is installed." -ForegroundColor Green
Write-Host "  program : $exePath"
Write-Host "  saves   : $(Join-Path $env:APPDATA $AppName)  (left untouched by uninstall)"
Write-Host "  remove  : Settings > Apps, or run uninstall.ps1 in the install folder"
Write-Host ""

if (-not $Silent) {
    $answer = Read-Host "Launch $AppName now? [Y/n]"
    if (-not $answer -or $answer -match '^[YyΝν]') {
        Start-Process -FilePath $exePath -WorkingDirectory $Dir
    }
}
exit 0
