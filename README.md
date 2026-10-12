# Start Bar Controller

Move the Windows taskbar to the **top, left, right or bottom** of the screen, with the start
menu following it, and adjust the rest of the taskbar from one place. On Windows 11 it keeps
the **real Windows 11 taskbar and start menu**.

![Position page](docs/position.png)

## What it does

- **Taskbar position:** pick an edge, or click one on the preview. On Windows 11 the taskbar
  and start menu keep their Windows 11 look.
- **Layout for moved taskbars:** vertical taskbar width, where the start menu and jump lists
  open along a side taskbar, running indicators on top, start menu animation.
- **Taskbar settings:** alignment, auto-hide, Search, Task view, combine buttons (main and
  other monitors), seconds in the clock, badges, flashing, "End task" in the right-click menu.
- **Notification-area icon:** right-click to move the taskbar in one click. It can start
  with Windows.
- **Restore original:** every value the app changes is backed up first. *Settings → Restore*
  (or uninstalling) puts it all back.
- **Light and dark mode:** follows Windows, using the Windows 11 (Fluent) style.
- **Command line:** `StartBarController --edge top`, and more (see below).

## How the taskbar is moved

Windows 11 has no supported setting for moving its taskbar (Microsoft is testing one in
Insider preview builds). So the app has three engines, and **Automatic** picks the right one:

| Engine | Look | Edges | Notes |
|---|---|---|---|
| **Windhawk** (default on Windows 11) | Windows 11 taskbar and start menu | all four | Uses two free, open-source [Windhawk](https://windhawk.net) mods by m417z: *Taskbar on top for Windows 11* and *Vertical Taskbar for Windows 11*. Changes apply in seconds, no Explorer restart. The mods use Windows' own position support on builds that have it. |
| **ExplorerPatcher** | Windows 10 taskbar | all four | [ExplorerPatcher](https://github.com/valinet/ExplorerPatcher) swaps in the Windows 10-style taskbar. Its Windows 10 start menu follows the taskbar (not on 24H2 and later). Explorer restarts on each change. |
| **Windows built-in** | unchanged | all on Windows 10; on Windows 11, only preview builds with the new position feature (21H2: top, partly) | Writes Windows' own setting and restarts Explorer. |

The app sets up the engine for you:
- **Windhawk not installed:** *Install Windhawk* runs `winget install RamenSoftware.Windhawk`.
- **A mod missing:** with Windhawk 2.0 (which has `windhawk-cli.exe`) the app installs it
  automatically. With Windhawk 1.x, install it once from Windhawk's *Explore* page.
- **Switching engines:** the old engine's changes are undone, so two engines never fight
  over the taskbar.
- **Admin rights:** Windhawk keeps its mod settings machine-wide, so changing them asks
  for permission once (UAC). For that the app starts a short-lived elevated copy of itself,
  which accepts changes only to those two mods.

Limitations to know about:
- The vertical Windhawk mod doesn't support auto-hide.
- With the top taskbar, the Action Center (Win+A) may still open at the bottom.
- After Windows updates, Windhawk mods sometimes need an update. Windhawk handles that itself.

## Install

From the repository's **Actions** tab (latest *Build* run → *Artifacts*), or from *Releases*
once a version is tagged:

- **StartBarController-Setup** is the installer. It installs for the current user without
  admin rights, adds a Start menu shortcut, and can start with Windows. Uninstalling
  restores the original taskbar.
- **StartBarController-portable** is a single `.exe` that runs without installing.

Both are self-contained (no .NET install needed) and run on Windows 10 and 11 x64. The files
aren't code-signed yet, so SmartScreen may warn: *More info → Run anyway*.

## Command line

```
StartBarController --status
StartBarController --edge <top|bottom|left|right> [--engine <auto|windhawk|explorerpatcher|native>]
StartBarController --set <setting>=<value> [--set ...]     e.g. --set search=0 --set alignment=0
StartBarController --autohide <on|off>
StartBarController --restore
StartBarController --list-settings
StartBarController --tray                                  start in the notification area
```

`--no-restart` skips restarting Explorer for changes that need it. Exit codes: `0` success,
`1` not possible on this system, `2` bad arguments.

## How it works

- **Windhawk:** the app finds Windhawk the way Windhawk does (`windhawk.ini`, then the
  engine's `engine.ini`). It then writes each mod's `Disabled` flag and `Settings`, plus a
  new `SettingsChangeTime` stamp, under `HKLM\SOFTWARE\Windhawk\Engine\Mods\<mod>` (or the
  mod's INI file for portable installs). Windhawk watches that storage and reloads the mod.
- **Windows 10 / ExplorerPatcher:** byte 12 of `HKCU\...\Explorer\StuckRects3\Settings`
  (and each monitor's copy under `MMStuckRects3`) holds the edge: 0 left, 1 top, 2 right,
  3 bottom. Explorer reads it only at startup, so the app kills Explorer and lets Windows
  restart it (a clean exit would save the old position over the new one). ExplorerPatcher's
  taskbar style is `HKCU\Software\ExplorerPatcher\OldTaskbar`.
- **Preview builds with native positions:** `HKCU\...\Explorer\Advanced\TaskbarLocation`.
- **Taskbar settings:** documented per-user values (`TaskbarAl`, `SearchboxTaskbarMode`, …),
  followed by a `WM_SETTINGCHANGE "TraySettings"` broadcast so Explorer picks them up.

## Building

Requires the .NET 10 SDK. The core library and tests build and run on any OS. The app
targets Windows, but it also compiles on macOS and Linux (`EnableWindowsTargeting`).

```
dotnet test tests/StartBarController.Core.Tests
dotnet publish src/StartBarController.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
ISCC.exe /DAppVersion=0.9.0 installer\StartBarController.iss     (Inno Setup 6, optional)
```

Layout:
- `src/StartBarController.Core`: everything that decides what to change. That covers the
  engines and planner, the Windhawk storage, taskbar settings, backup and restore, and the
  CLI. It's platform-neutral and covered by xUnit tests in `tests/`.
- `src/StartBarController.App`: the WPF app (pages, preview, tray icon), the Windows
  implementations (registry, Explorer, elevation), and `--smoke-test`, which CI uses to open
  every page and save screenshots.
- `installer/`: the Inno Setup script.

## Credits

The Windows 11 taskbar positioning comes from the Windhawk mods *Taskbar on top for
Windows 11* and *Vertical Taskbar for Windows 11* by [m417z](https://github.com/m417z)
(GPL-3.0), run by [Windhawk](https://windhawk.net). The Windows 10-style taskbar comes from
[ExplorerPatcher](https://github.com/valinet/ExplorerPatcher) by valinet. They're separate
projects, and this app doesn't include their code; it installs and configures them.
