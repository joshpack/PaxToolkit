# PAX App Standard

Every PAX app follows these rules.

1. **Own title bar.** No system title bar. A 38 px dark bar with the app icon, the app name, and minimize / maximize (resizable windows only) / close. Close turns red on hover. Empty space drags the window, double-click maximizes. Dialogs too.
2. **Remembers its window.** Size (if resizable), position and maximized state for every window, restored on next open, and ignored if that spot is no longer on any screen.
3. **Works with PAX Toolkit.**
   - Launching the exe again while it runs opens its window (single instance).
   - Apps with a tray icon read `%APPDATA%\PAX\toolkit.json` → `apps.<AppId>.trayIcon` (default `true`), watch the file and show or hide the icon live. The app's own settings have the same "Show tray icon" switch, writing the same file. With the icon hidden, the app keeps running and its window opens from PAX Toolkit or by starting it again.
   - The Toolkit catalog marks these apps with `"tray": true`.
4. **Own icon** (`BrandIcon.cs`), neon theme, `--self-test` that never shows anything on screen, README with current screenshots.

Avalonia apps copy [`PaxStandard.cs`](PaxStandard.cs) unchanged. Auto Clicker (WinForms) and Startup Scanner (PowerShell/WPF) follow the same rules in their own toolkit.

Status: draft. `PaxStandard.cs` has not been compiled into any app yet.
