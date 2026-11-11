# TempApp

A tiny Windows 11 tray app that shows live CPU and iGPU temperatures as numbers in the
notification area (e.g. `81°`), coloured green (< 60 °C), yellow (60–80 °C) or red (> 80 °C).

Built with C# / .NET 8 Windows Forms and [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor).
Developed and tested on an Intel Core Ultra 5 225H with Intel Arc 130T integrated graphics.

## Requirements

- Windows 10/11 x64
- **PawnIO driver** — LibreHardwareMonitor 0.9.5+ uses it for CPU register (MSR) access.
  Without it, every temperature shows `--`. Install once:
  ```
  winget install namazso.PawnIO
  ```
  (or download from https://pawnio.eu)
- Administrator rights — the exe requests elevation (UAC prompt) when started manually.
- To build: .NET 8 SDK (`winget install Microsoft.DotNet.SDK.8`)

## Download

Grab `TempApp.exe` from the [Releases](../../releases) page. It's self-contained (no .NET install
needed); you still need PawnIO (above).

## Run

Copy `TempApp.exe` anywhere permanent (e.g. `C:\Tools\TempApp\`) and run it.

An icon showing the CPU package temperature appears in the tray (it may start in the `^`
overflow — drag it onto the taskbar). Hover it for details:

```
CPU package: 81°C (core max 72°C)
iGPU: 81°C (shares CPU package)
CPU power: 17.1 W
iGPU power: 0.04 W
```

**iGPU temperature:** the Arc 130T exposes no temperature sensor to LibreHardwareMonitor, so the
iGPU line shows the CPU package temperature (the iGPU is on the same package). If a future
driver/library version adds a real sensor, it's used automatically.

A second, iGPU-only icon is available from the menu but off by default, since without a real iGPU
sensor it would just repeat the CPU number.

### Right-click menu

- **Celsius / Fahrenheit**
- **Update interval** — 1 s, 2 s (default), 5 s
- **Show separate iGPU icon** — off by default
- **Start with Windows** — creates/removes a Task Scheduler task (see below)
- **Exit**

Only one instance can run at a time.

### Settings

Stored in `%AppData%\TempApp\settings.json` (created on first change):

```json
{
  "Fahrenheit": false,
  "IntervalMs": 2000,
  "ShowGpuIcon": false,
  "Font": "Bahnschrift SemiBold SemiConden"
}
```

`Font` accepts any installed font family name (falls back to Segoe UI). Restart the app after
editing the file by hand.

### Start with Windows

Because the app needs admin, a `Run` registry key would cause a UAC prompt at every logon.
Instead, "Start with Windows" registers a scheduled task named **TempApp** that runs at logon with
highest privileges (no prompt), also on battery, with no time limit.

The task stores the exe's **current path** — enable it from the copy you intend to keep. If you move
the exe, toggle the option off and on again.

Command-line equivalents (run from an elevated prompt):
```
TempApp.exe --install-startup
TempApp.exe --remove-startup
```

## Build

```
cd C:\Projects\TempApp
dotnet build                                          # Debug build of both projects
dotnet publish TempApp -p:PublishProfile=win-x64      # -> publish\TempApp.exe
```

The publish profile (`TempApp\Properties\PublishProfiles\win-x64.pubxml`) produces a single
self-contained exe (~160 MB). Notes:

- Trimming is not supported for Windows Forms, so the full runtime is included.
- `EnableCompressionInSingleFile` would shrink it to ~70 MB but costs ~34 MB more RAM at runtime
  (measured 105 MB vs 71 MB private), so it's off.
- A few native runtime DLLs are extracted to `%TEMP%\.net\TempApp\` on first run.

Typical footprint: ~70 MB private memory, < 0.1 % CPU at the 2 s interval. Most of the memory is
the .NET runtime plus LibreHardwareMonitor.

## Project layout

```
TempApp.sln
TempApp\
  Program.cs            entry point, single-instance mutex, tray icons, menu, tooltip
  SensorReader.cs       LibreHardwareMonitor wrapper (CPU package/core max temp, power; iGPU power)
  TrayIconRenderer.cs   draws "81°" onto a tray-sized icon; frees HICONs (DestroyIcon)
  Settings.cs           JSON settings + Task Scheduler startup task
  app.manifest          requireAdministrator
SensorDump\             diagnostic console app: lists every sensor LibreHardwareMonitor sees
```

## Troubleshooting

- **Icons show `--`** — PawnIO isn't installed, or the app isn't elevated. Run the sensor dump
  from an elevated prompt to see what's readable:
  ```
  dotnet run --project SensorDump -c Release -- sensors.txt
  ```
- **Icons don't appear** — check the `^` overflow, or Settings → Personalization → Taskbar →
  Other system tray icons, and switch TempApp on.
- **"TempApp is already running"** — it's in the tray already; use Exit from its menu first.

## Uninstall

1. Right-click → untick **Start with Windows** (or run `TempApp.exe --remove-startup` elevated).
2. Exit, then delete the exe and `%AppData%\TempApp\`.
3. Optionally uninstall PawnIO (`winget uninstall namazso.PawnIO`) if nothing else uses it
   (FanControl and LibreHardwareMonitor do).

## Credits

- [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) (MPL-2.0) —
  sensor access; bundled inside the published exe.
- [PawnIO](https://pawnio.eu) — kernel driver used by LibreHardwareMonitor (installed separately).
