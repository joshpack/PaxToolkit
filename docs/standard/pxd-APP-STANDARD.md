# pxd App Standard

Every pxd app follows these rules.

1. **Own title bar.** No system title bar. A 38 px dark bar with the app icon, the app name, and minimize / maximize (resizable windows only) / close. Close turns red on hover. Empty space drags the window, double-click maximizes. Dialogs too.
2. **Remembers its window.** Size (if resizable), position and maximized state for every window, restored on next open, and ignored if that spot is no longer on any screen.
3. **Works with pxd Toolkit.**
   - Launching the exe again while it runs opens its window (single instance).
   - Apps with a tray icon read `%APPDATA%\pxd\toolkit.json` → `apps.<AppId>.trayIcon` (default `true`), watch the file and show or hide the icon live. The app's own settings have the same "Show tray icon" switch, writing the same file. With the icon hidden, the app keeps running and its window opens from pxd Toolkit or by starting it again.
   - The Toolkit catalog marks these apps with `"tray": true`.
4. **Own icon** (`BrandIcon.cs`), neon theme, `--self-test` that never shows anything on screen, README with current screenshots.
5. **Moves its own leftovers.** On first run after a rename, an app moves its settings folders and startup entries from its former name (`pxdMigrate` in pxdStandard.cs: `Folder`, `RunEntry`, and the shared toolkit.json). Every move is safe to repeat and never throws. The Toolkit moves install folders the same way.
6. **Updates itself before it opens.** First thing in Main (before the single-instance check): `if (pxdUpdate.BeforeStart("pxdtek/<repo>", "<release asset regex>", args)) return;`. It asks GitHub (4 s at most, skipped offline, for dev builds in bin and for --self-test), installs a newer release in place (SHA-256 checked, running files set aside as *.pxdold and removed next start) and restarts the app with the same arguments. PXD_NO_UPDATE=1 turns it off.

Avalonia apps copy [`pxdStandard.cs`](pxdStandard.cs) unchanged. Auto Clicker (WinForms) and Startup Scanner (PowerShell/WPF) follow the same rules in their own toolkit.

Tests render windows with Avalonia.Headless only: a window moved far off screen still flashes on screen for a moment.
