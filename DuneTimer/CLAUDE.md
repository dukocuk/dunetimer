# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

- Build: `dotnet build DuneTimer.csproj`
- Run: `dotnet run --project DuneTimer.csproj` (run from a terminal — see console note below)
- Publish: `dotnet publish -c Release -r win-x64` (csproj already pins `PublishSingleFile`, `SelfContained=true`, `IncludeNativeLibrariesForSelfExtract`)
- Release: push a `vX.Y.Z` tag (`git tag vX.Y.Z && git push origin vX.Y.Z`) — `.github/workflows/release.yml` builds, zips, and creates the GitHub Release automatically, versioning from the tag itself (`-p:Version=`), not from the csproj's `<Version>`. No manual publish/zip/upload needed.
- No test project exists in the solution — there is nothing to run for tests.
- `DuneTimer.sln` lives one directory above this repo root (`..\DuneTimer.sln`). It references this single project, but it isn't tracked in this git repo, so day-to-day commands should target `DuneTimer.csproj` directly.
- Windows-only target (`net9.0-windows10.0.19041.0`, WPF, Win32 interop) — cannot build or run on non-Windows.

## Architecture

DuneTimer is a WPF overlay app (MVVM: `Models/`, `ViewModels/`, `Views/`, `Services/`, `Helpers/`) that tracks in-game crafting timers for Dune: Awakening, either entered manually or read off-screen via Tesseract OCR.

1. **Manual DI in `App.xaml.cs`, no container.** Services are constructed bottom-up and wired together by hand; construction order matters. Cross-cutting coupling goes through settable `Func`/`Action` properties on services/viewmodels (`HideOverlay`, `ShowOverlay`, `GetOverlayBounds`, `IsOverlayVisible`, `OpenSettingsRequested`, `CloseWindow`) rather than events or a DI framework — follow this pattern rather than introducing a container.

2. **Scanner threading contract** (`ScreenScannerService`): `DetectAndMerge` and `ScanRegionOnce` are pure background-thread functions — they return plain data and never touch `TimerService`, `SettingsService`, or UI directly. All mutation of shared state happens after `await`, back on the UI thread. `_isTicking`/`_isAutoDetecting` are mutually exclusive and are what make it safe to read `_settings` from the background pass. `SettingsService.GetScanRegions()` returns a defensive copy for the same reason. `_ocrLock` serializes access to the single shared `TesseractEngine`.

3. **Anchor-based OCR pipeline**, the core detection flow: full-screen sparse OCR → match a line against `QUEUE`, `EXTRACTION`, or `PROCESSING` (`TextParserService.MatchAnchor`) → `BuildAnchorRegion` builds a *full-width* region from y=0 down through the anchor (the highlighted station/building tab name sits at the top of the screen, anywhere across the width) → `ResolveAnchoredTimer`/`ResolveCapacityTimer` does a ±250px column-restricted search for the value *because* the region is full-width and can otherwise catch a parallel panel at a similar height → the resolved `(name, seconds)` is upserted via `AddOrUpdateAnchoredTimer`, keyed on `(regionId, name)`, with a name-only adoption fallback for detached timers (region pruned/replaced). `ProbationMissLimit` only prunes a region that has *never once* matched.

   `ProcessingCapacity` (Blood Purifier) is a computed variant: the panel never displays a countdown, only a draining capacity value and a rate. Direct testing showed the rate line ("PROCESSING RATE"/"X ml/s") fails to OCR at all, consistently, across every attempt — unlike the capacity value beside it, which reads cleanly every time — so `ResolveCapacityTimer` reads only the capacity value and looks up a hardcoded rate (`TextParserService.ProcessingRatesMlPerSec`) by resolved station name, computing `seconds = capacity / rate`. Don't try to make it OCR the rate instead; that line just doesn't come through.

4. **Data files load from `AppContext.BaseDirectory` (the build output dir), not the source tree.** Editing `recipes.json`/`stations.json` in the repo requires a rebuild before the running app picks it up. `stations.json` is the file to extend when a station/building name goes unrecognized — `ResolveAnchoredTimer` logs a console hint naming exactly this when it happens.

5. **Settings migration.** `SettingsService.Load()` bumps `SettingsVersion` (currently 4) and clears/reseeds state whenever a `ScanRegion`'s meaning changes shape — e.g. anchor detection changed what a region represents. Any change to how regions are built or interpreted needs a matching version bump and migration branch, or stale on-disk regions silently keep behaving the old way. `settings.json` is gitignored (user-local: hotkeys, regions, mute state).

6. **Console is the only diagnostic channel, and it's conditional.** `NativeMethods.AttachConsole(ATTACH_PARENT_PROCESS)` in `App.OnStartup` routes `Console.WriteLine` to the parent terminal — `[Scanner]`/`[AutoDetect]` lines only appear when launched from a terminal, not from Explorer/double-click. `#if DEBUG` builds also drop `debug_capture.png` in the output dir showing the preprocessed OCR capture.

7. **Two coordinate spaces.** Screen capture and `GetSystemMetrics` (`ScreenScannerService`, `NativeMethods`) are physical pixels; WPF window bounds (`Window.Left/Top/ActualWidth`) are DIPs. `App.GetOverlayPhysicalRect` is the conversion between them — anything touching region/overlay math needs to know which space it's in.

8. **Global hotkeys** are Win32 `RegisterHotKey` (`HotkeyService`), user-rebindable via Settings, persisted in `settings.json`, with defaults in `HotkeyActions.Defaults()`. Rebinding at runtime goes through `HotkeyService.Reregister` (unregister-all then re-register), not a restart.

9. No `Assets/` directory exists in the repo — `SoundService` falls back to `SystemSounds.Beep` until a user supplies a custom `.wav` via Settings.

## Verification

There is no test suite. To check a change:
- `dotnet build DuneTimer.csproj` to confirm it compiles.
- `dotnet run --project DuneTimer.csproj` from a terminal (not Explorer) to see `[Scanner]`/`[AutoDetect]` console output.
- Exercise the golden path manually: launch, Alt+T to show/hide the overlay, Alt+D to auto-detect against a live game panel, Alt+S to start scanning, Alt+N for a manual timer via the Control Panel.
- OCR/anchor correctness can't be verified without the actual game running on screen.
