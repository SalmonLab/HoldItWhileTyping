# HoldItWhileTyping

This project is a Windows tray app that keeps the current window active for a short time after input activity, reducing the chance of another app stealing focus while you are typing.

## What it does

- Runs as a tray app with an icon.
- Detects keyboard and mouse button input (low-level hooks).
- While input has happened in the last configured timeout window, it restores the previous foreground window if a different window becomes active.
- Allows configuring the timeout from the tray menu:
  - 800 ms
  - 1200 ms
  - 2000 ms
  - 3000 ms
  - 5000 ms
- Supports **Transparent mode**:
  - While enabled, it does not enforce focus restoration even when typing is active.
  - Useful when you want to temporarily allow normal focus changes.
- Supports an **excluded-app list**:
  - Process names in this list are excluded from focus lock (no auto restoration to previous window).
  - Useful example: set `teams`, `slack`, or `discord` so popups/notification apps can be focused while you type.
- Excluded-app list can be edited from tray menu (`Excluded app list...`) and is stored in settings.
- Stores settings in:
  - `%LocalAppData%\HoldItWhileTyping\settings.json`

Example settings:
```json
{
  "Enabled": true,
  "LockMilliseconds": 2000,
  "TransparentMode": false,
  "ExcludedProcesses": [
    "teams",
    "slack"
  ]
}
```

## Build and run

```powershell
dotnet build
dotnet run
```

For release packaging:

```powershell
dotnet publish -c Release -r win-x64 -p:PublishSingleFile=true --self-contained false
```

## Notes

- Tray icon has only app-level control (Enable/Disable and timeout).
- The app does not log keystrokes; it only tracks key/mouse event timing.
- Windows foreground behavior differs by app; this implementation restores focus immediately when a change is detected.
