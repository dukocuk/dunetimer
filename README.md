# DuneTimer

A lightweight Windows overlay for **Dune: Awakening** that tracks your crafting and refinery timers — either added manually or detected automatically from your screen with OCR.

## Features

- **Manual timers** for any of the 41 built-in recipes (ore refining, chemical/spice refining, water processing, blood purifiers, and more).
- **OCR auto-detect**: point it at a crafting station panel and it reads the countdown for you — no typing required.
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

DuneTimer runs in the background with global hotkeys (these work even while the game is focused), and can be rebound in Settings:

| Hotkey | Action |
| --- | --- |
| `Alt+T` | Show/hide the overlay |
| `Alt+N` | Add a manual timer |
| `Ctrl+Alt+R` | Set an OCR scan region |
| `Alt+D` | Run auto-detect against a crafting panel |
| `Alt+S` | Start the OCR scanner |
| `Alt+X` | Toggle click-through / interactive mode |

**Quick start — manual timer:** press `Alt+N`, pick a recipe from the list, and it starts counting down on the overlay.

**Quick start — auto-detect:** open a crafting/refinery panel in-game, then press `Alt+D` (or `Alt+S` to keep scanning continuously). DuneTimer reads the station name and countdown directly off the screen and adds it to the overlay automatically.

## Building from source

Requires the [.NET 9 SDK](https://dotnet.microsoft.com/download) and Windows (this is a WPF app with Win32 interop, so it can't be built or run on other platforms).

```
dotnet build DuneTimer.csproj
dotnet run --project DuneTimer.csproj
```

To produce a release build:

```
dotnet publish -c Release -r win-x64
```
