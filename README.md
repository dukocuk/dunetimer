# DuneTimer

A lightweight Windows overlay for **Dune: Awakening** that tracks your crafting and refinery timers — either added manually or detected automatically from your screen with OCR.

## Features

- **Manual timers** for any of the 41 built-in recipes (ore refining, chemical/spice refining, water processing, blood purifiers, and more).
- **OCR auto-detect**: point it at a crafting station panel and it reads the countdown for you — no typing required. It reads pixels off your screen like a screenshot; it never reads the game's process memory.
- **Always-on-top overlay** — a small, draggable HUD showing every active timer with a countdown and progress bar.
- **Alerts** — plays a sound (or falls back to a system beep) when a timer finishes, with per-timer and global mute.
- **Rebindable global hotkeys**, configurable from the in-app Settings window.

## Requirements

- Windows 10 (version 1903+) or Windows 11
- Nothing else — the release build is self-contained and does not require installing .NET separately.

## Install

1. Go to the [Releases page](https://github.com/dukocuk/dunetimer/releases) and download the latest `DuneTimer-vX.Y.Z-win-x64.zip`.
2. Extract the zip anywhere.
3. Run `DuneTimer.exe`.

Windows SmartScreen may warn that the app is from an unrecognized publisher, since it isn't code-signed. Click **More info** → **Run anyway** to continue.

## Usage

DuneTimer has no main window — after launch it lives in the system tray and as a small always-on-top overlay, and its global hotkeys work even while the game is focused (rebindable in Settings).

| Hotkey | Action |
| --- | --- |
| `Alt+T` | Show/hide the overlay |
| `Alt+N` | Add a manual timer |
| `Alt+D` | Run auto-detect against a crafting panel |
| `Alt+S` | Start the OCR scanner (runs an auto-detect first) |
| `Alt+X` | Toggle click-through / interactive mode |

### Manual timers

Press `Alt+N` to open the Add Timer window. Either pick a **Category** and **Recipe** from the built-in list (set a **Quantity** to queue more than one), or fill in the "Custom Timer" fields (Name, Min, Sec) for anything not in the list, then click **Start Timer**. It appears on the overlay immediately.

### Auto-detect & OCR scanning

`Alt+D` runs a one-off scan; `Alt+S` runs that same auto-detect and then keeps scanning continuously, so you don't need to press `Alt+D` first. Both read the currently open crafting/refinery panel's station name and countdown directly off the screen — including Blood Purifier panels, which don't show a countdown and instead have their remaining time computed from the draining capacity value. **Dune: Awakening must be the focused/foreground window** for either to work; if it isn't, the overlay's status bar shows a message saying so instead of scanning.

The panel showing the timer has to actually be open and visible on screen — DuneTimer captures that area like a screenshot and runs OCR on it, so if the panel is closed or covered there's nothing to read. This is purely screen capture: the app never reads or writes the game's process memory, doesn't inject into it, and doesn't touch anything besides checking which window is currently focused.

### The overlay

The overlay is click-through by default so it doesn't block clicks to the game underneath. Press `Alt+X` to switch it to interactive mode — this is required before you can drag it by its header or click its buttons. Each timer row has a mute toggle and a ✕ to remove it. The bottom status bar shows the scanner's current status, a global mute button, a ⚙ button to open Settings, and an **Auto-Detect** button as a clickable alternative to `Alt+D`.

### Settings

Open Settings from the overlay's ⚙ button or the tray menu. From here you can rebind any of the five hotkeys above (click its box, then press a combo that includes Alt, Ctrl, or Shift), and set a custom alert sound — **Test** it, **Browse…** for a `.wav` file, or **Reset** to the default beep.

### System tray

DuneTimer keeps an icon in the system tray for the lifetime of the app. Right-click it for **Show/Hide Overlay**, **Settings**, and **Exit**, or double-click it to toggle the overlay.

## Building from source

Requires the [.NET 9 SDK](https://dotnet.microsoft.com/download) and Windows (this is a WPF app with Win32 interop, so it can't be built or run on other platforms).

```
dotnet build DuneTimer/DuneTimer.csproj
dotnet run --project DuneTimer/DuneTimer.csproj
```

To produce a release build:

```
dotnet publish DuneTimer/DuneTimer.csproj -c Release -r win-x64
```
