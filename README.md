# PAX Toolkit

One app for all the PAX apps. PAX Toolkit lists every PAX app on GitHub, turns them on (downloads and installs them) with one switch, keeps them up to date, and lets you open, start or close any of them from its own tray icon.

![PAX Toolkit](docs/screenshots/toolkit.png)

## Features

- **One switch per app.** Turn an app on and the toolkit downloads its latest release from GitHub, checks the SHA-256 checksum when the release has one, and installs it to `%USERPROFILE%\Apps\<App>`. Turn it off and the app is closed and removed. Its settings stay, so turning it back on picks up where you left off.
- **Updates.** The toolkit checks GitHub every 6 hours (or when you click *Check for updates*), marks apps with a new version and updates them one at a time or all at once. A running app is closed for the update and started again afterwards. You can let it install updates on its own.
- **Tray icon.** Every app you've turned on is in the tray menu: start it, start it in its own tray, close it, update it, or open its folder. An orange dot on the icon means updates are waiting.
- **Finds what you already have.** Apps already in your Apps folder, or installed with their own setup, show up as on.
- **.NET check.** If an app needs a .NET runtime you don't have, its card says so and can install it with winget.
- **Start menu shortcuts** in *Start → PAX Toolkit* for each app (optional).
- **New apps show up by themselves.** The app list is `catalog.json` in this repo, which the toolkit reads at every check.
- **Updates itself** when a new PAX Toolkit release is out.

## Apps

| App | What it does |
| --- | --- |
| [PAX Commander](https://github.com/joshpack/PaxCommander) | Dual-pane file manager |
| [PAX Media Manager](https://github.com/joshpack/PaxMediaManager) | Download mover, poster library and player |
| [PAX Audio Switcher](https://github.com/joshpack/PaxAudioSwitcher) | Audio device hotkeys and profiles |
| [PAX Auto Clicker](https://github.com/joshpack/PaxAutoClicker) | Hotkey auto clicker |
| [PAX Chatter Guard](https://github.com/joshpack/PaxChatterGuard) | Keyboard and mouse chatter filter |
| [PAX Startup Scanner](https://github.com/joshpack/PaxStartupScanner) | Startup items and boot time (PowerShell, runs as admin) |
| [PAX Watermark](https://github.com/joshpack/pax_watermark) | Watermark one photo or a whole folder |
| [PAX Diagnostics](https://github.com/joshpack/Pax_Diagnostics) | System info, SFC/DISM repairs, CBS log filter |

## Supported systems

| OS | Status |
| --- | --- |
| Windows 10 / 11 (x64) | Supported |
| macOS, Linux | Not supported (the apps it installs are Windows builds) |

## Install

1. Download `PaxToolkit-<version>-win-x64.zip` from [Releases](https://github.com/joshpack/PaxToolkit/releases) and unzip it.
2. Run `PaxToolkit.exe`. It is a single self-contained file, so no .NET install is needed.
3. Click **Move it there** on the banner to keep it in `%USERPROFILE%\Apps\PaxToolkit` with a Start menu shortcut, and tick *Start PAX Toolkit with Windows* under **Settings** if you want the tray icon at sign-in.

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

Needs the .NET 10 SDK. `--self-test <folder>` runs the engine against GitHub with the install paths pointed elsewhere (set `PAXTK_DATA`, `PAXTK_APPS` and `PAXTK_STARTMENU` first), installs and removes a small app, and renders the window off-screen.

## Adding an app

Add an entry to `catalog.json`: the GitHub repo, a regex for the release asset (`zip` or a PowerShell `script`), the file to start and its folder name. Optional: `openArgs`, `trayArgs` (shows *Start in tray*), `guiArgs`, `elevate`, `knownPaths` and `uninstall` for copies installed by their own setup. Put a 256 px PNG in `Assets/icons/<id>.png` for its picture, or it gets a tile with its initials.
