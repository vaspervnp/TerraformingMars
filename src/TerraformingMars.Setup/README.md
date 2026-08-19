# TerraformingMars.Setup — installer builder

Turns the **published** binaries into ready-to-ship installers, one per platform. It does not build
the game: publish first (the profiles in `src/TerraformingMars.Game/Properties/PublishProfiles`
write straight into `C:\Deploy\TerraformingMars`), then run this.

```bash
dotnet run --project src/TerraformingMars.Setup
```

```
C:\Deploy\TerraformingMars\            <- source (--source)
    WinX64\      TerraformingMars.Game.exe + Assets + Content
    LinuxX64\    TerraformingMars.Game     + Assets + Content
    LinuxArm\    TerraformingMars.Game     + Assets + Content
    Installers\                        <- output (--out)
        MarsTerraforming-1.2-Setup-win-x64.exe
        MarsTerraforming-1.2-Setup-linux-x64.sh
        MarsTerraforming-1.2-Setup-linux-arm.sh
```

Options: `--source DIR`, `--out DIR`, `--targets win-x64,linux-x64,linux-arm`, `--version 1.2`
(by default the version is read from the published Windows exe). A target whose folder is missing is
skipped with a message, so you can build just the platforms you have.

## What the installers do

| | Windows | Linux / Linux ARM |
|---|---|---|
| Format | self-extracting `.exe` (IExpress — part of Windows, no WiX/Inno needed) | self-extracting `.sh` (tar.gz appended after a marker line, `makeself` style) |
| Installs to | `%LOCALAPPDATA%\Programs\Mars Terraforming` (per user, **no admin**) | `/opt/mars-terraforming` as root, otherwise `~/.local/share/mars-terraforming` |
| Menu entry | Start menu shortcut | `mars-terraforming.desktop` in the applications menu |
| Desktop icon | shortcut on the desktop | `.desktop` on the desktop (honours `XDG_DESKTOP_DIR`, marked trusted for GNOME) |
| Icon | the exe's own icon | the 256px PNG pulled out of `Icon.ico`, installed into `hicolor` |
| Extras | entry in *Apps & features* with an uninstaller | `mars-terraforming` symlink in `~/.local/bin`, `uninstall.sh` in the install folder |

Saved games live in the user's account folder (see `SaveManager`) and are **never** touched by
install or uninstall.

## Running them

* **Windows** — double-click the `.exe`. A console window walks through the install and offers to
  launch the game. To script it, extract first and call the script with parameters:

  ```
  MarsTerraforming-1.2-Setup-win-x64.exe /C /T:%TEMP%\tm /Q
  powershell -ExecutionPolicy Bypass -File %TEMP%\tm\install.ps1 -Silent -Dir "C:\Games\Mars Terraforming" -NoDesktopIcon
  ```

* **Linux** — `sh MarsTerraforming-1.2-Setup-linux-x64.sh` (or `chmod +x` it first). Options:
  `--prefix DIR`, `--no-desktop-icon`, `-y`. Run it with `sudo` for a system-wide install.
  Audio needs the system OpenAL (`sudo apt install libopenal1`).

## Notes for maintainers

* The install/uninstall scripts live in `Templates/` and are embedded in the tool; `@PLACEHOLDER@`
  tokens (app name, version, exe name…) are filled in when an installer is built.
* The Windows installer can only be built on Windows (IExpress); the Linux ones build anywhere.

### IExpress traps (all of them fail silently — cost a full debugging round each)

| Trap | Symptom | Rule |
|---|---|---|
| Quoted `.sed` path on the command line | `iexpress` exits with `1`, builds nothing | run it *inside* the staging folder and pass a bare `setup.sed` |
| `AppLaunched=cmd /c install.cmd` | the package extracts, runs, and does **nothing at all** | `AppLaunched` must name a file **inside** the package: `AppLaunched=install.cmd` (IExpress already sets the extraction folder as the working directory) |
| `ShowInstallProgramWindow=1` | install runs invisibly; any prompt hangs forever | `0` = **visible** window, `1` = hidden |
| `.cmd` written with LF endings | `': was unexpected at this time'`, or `'install.cmd' is not recognized` | batch files need **CRLF** and no BOM; `.ps1` wants CRLF **with** BOM so non-ASCII comments survive Windows PowerShell 5.1 |
