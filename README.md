# pxd Toolkit

One app for all the pxd apps. pxd Toolkit lists every pxd app on GitHub, installs any of them with one click, keeps them up to date, lets you open, start or close them, and decides which of them show a tray icon.

![pxd Toolkit](docs/screenshots/toolkit.png)

## Features

- **Clean layout.** Installed apps and available apps in a slim sidebar (a green dot means running, an orange one means an update is waiting); the selected app gets one clear main button (Install, Update or Open) with the rest tucked below.
- **One-click install.** The toolkit downloads the app's latest release from GitHub, checks the SHA-256 checksum when the release has one, and installs it to `%USERPROFILE%\Apps\<App>`. Uninstall closes and removes it; its settings stay, so installing it again picks up where you left off.
- **Updates.** The toolkit checks GitHub every 6 hours (or when you click ↻), and updates apps one at a time or all at once. A running app is closed for the update and started again afterwards. You can let it install updates on its own.
- **Tray icons on or off.** Every pxd app with a tray icon has a *Tray icon* switch here. Off, the app keeps running without an icon; open it from the toolkit. The app's own settings have the same switch, and both stay in sync (the [pxd App Standard](docs/standard/pxd-APP-STANDARD.md)).
- **Its own tray icon.** Every installed app is in the tray menu: start it, start it in its own tray, close it, update it, or open its folder. A green dot on the icon means updates are waiting.
- **Finds what you already have.** Apps already in your Apps folder, or installed with their own setup, show up as installed.
- **PowerShell one-liners.** *PowerShell one-liners* at the top of the sidebar lists every pxd command (`irm … | iex`) with a Copy button: pxd Toolkit, Startup Scanner, Event Viewer, and any app added later with one. Each app's page shows its own too.
- **.NET check.** If an app needs a .NET runtime you don't have, the toolkit says so and can install it with winget.
- **Start menu shortcuts** in *Start → pxd Toolkit* for each app (optional).
- **New apps show up by themselves.** The app list is `catalog.json` in this repo, which the toolkit reads at every check.
- **Updates itself** when a new pxd Toolkit release is out.

## Apps

| App | What it does |
| --- | --- |
| [pxd Commander](https://github.com/joshpack/pxdCommander) | Dual-pane file manager |
| [pxd Media Manager](https://github.com/joshpack/pxdMediaManager) | Download mover, poster library and player |
| [pxd Audio Switcher](https://github.com/joshpack/pxdAudioSwitcher) | Audio device hotkeys and profiles |
| [pxd Auto Clicker](https://github.com/joshpack/pxdAutoClicker) | Hotkey auto clicker |
| [pxd Chatter Guard](https://github.com/joshpack/pxdChatterGuard) | Keyboard and mouse chatter filter |
| [pxd Startup Scanner](https://github.com/joshpack/pxdStartupScanner) | Startup items and boot time (PowerShell, runs as admin) |
| [pxd Watermark](https://github.com/joshpack/pxdWatermark) | Watermark one photo or a whole folder |
| [pxd Diagnostics](https://github.com/joshpack/pxdDiagnostics) | System info, SFC/DISM repairs, CBS log filter |
| [pxd Windows Tweaks](https://github.com/joshpack/pxdWindowsTweaks) | Tweaks with undo, debloat, winget installer, fixes, update modes, DNS |
| [pxd Event Viewer](https://github.com/joshpack/pxdEventViewer) | PC snapshot for tickets, event logs in plain English |

## Supported systems

| OS | Status |
| --- | --- |
| Windows 10 / 11 (x64) | Supported |
| macOS, Linux | Not supported (the apps it installs are Windows builds) |

## Install

### Install with PowerShell

```powershell
irm pxdtek.com/tk/install | iex
```

No admin needed. It downloads the latest release, checks its SHA-256, puts `pxdToolkit.exe` in `%USERPROFILE%\Apps\pxdToolkit`, adds a Start menu shortcut (*Start → pxd Toolkit*) and starts it in the tray. Its window opens only if you answer yes when it asks. Run it again to update. Options go in environment variables first, for example `$env:PXDTK_HOME = 'D:\Tools\pxdToolkit'` (also `PXDTK_NOSTART=1`, `PXDTK_NOSHORTCUT=1`).

### The PowerShell tools, nothing installed

```powershell
irm pxdtek.com/tk | iex
```

On its own (also `irm pxdtek.com/i | iex`), the same script installs nothing unless you pick pxd Toolkit in its list and answer yes: it lists the pxd apps that have a PowerShell edition, shows whether each is installed on this PC (and whether an update is out), and runs them in memory. Nothing is saved on the PC, and the tools it runs are told to save nothing either (they only write a file when you ask them to save one). The script is [`install.ps1`](install.ps1); the full address works too:

```powershell
irm https://raw.githubusercontent.com/joshpack/pxdToolkit/main/install.ps1 | iex
```

### By hand

1. Download `pxdToolkit-<version>-win-x64.zip` from [Releases](https://github.com/joshpack/pxdToolkit/releases) and unzip it.
2. Run `pxdToolkit.exe`. It is a single self-contained file, so no .NET install is needed.
3. Click **Move it there** under **Settings** to keep it in `%USERPROFILE%\Apps\pxdToolkit` with a Start menu shortcut, and tick *Start pxd Toolkit with Windows* if you want the tray icon at sign-in.

## Command line

| Option | Does |
| --- | --- |
| `--minimized` | Start in the tray without opening the window |
| `--show` | Open the window (a second launch hands this to the running copy) |

## Build from source

```
dotnet build -c Release
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

Needs the .NET 10 SDK. `--self-test <folder>` runs the engine against GitHub with the install paths pointed elsewhere (set `PXDTK_DATA`, `PXDTK_APPS` and `PXDTK_STARTMENU` first), installs and removes a small app, and renders the window off-screen.

## Adding an app

Add an entry to `catalog.json`: the GitHub repo, a regex for the release asset (`zip` or a PowerShell `script`), the file to start and its folder name. Optional: `installCommand` (a PowerShell one-liner, shown with a Copy button on its page and in *PowerShell one-liners*) and `commandNote` (one line on what it does), `tray` (the app follows the pxd App Standard tray link, so the toolkit shows its *Tray icon* switch), `openArgs`, `trayArgs` (shows *Start in tray*), `guiArgs`, `elevate`, `knownPaths` and `uninstall` for copies installed by their own setup. Put a 256 px PNG in `Assets/icons/<id>.png` for its picture, or it gets a tile with its initials.
