# Development overview

MiVibe Remote is a Windows 11 WinForms tray application with a separate BLE/audio bridge and, in 0.3, an optional elevated enhanced-key helper.

## Architecture

- `MiVibe.Remote.Tray` owns the tray icon, status window, battery display, startup preference, and reconnect policy.
- `MiVibe.Remote.GattProbe` connects to the paired BLE remote, negotiates ATVV 1.x, decodes microphone audio, and renders PCM to VB-CABLE.
- The release keeps the bridge as a separate process so it can be stopped safely and restarted after a disconnect.
- `MiVibe.Remote.KeyBridge` observes the tested RC003 synchronous raw HID path. It identifies a report stream through Back / Volume + / Volume − press-and-release calibration and sends only allowlisted action events to the tray. The tray sends Backspace/Delete or foreground-gated Codex navigation.
- The key helper has a separate connection lifecycle and must be enabled manually after each tray launch. Driver-host reconnect resets calibration; failed calibration does not enable key actions. TV stays on its original Windows input path.
- Pause stops voice, enhanced keys, and the tray-owned Home/Menu mappings. Resume through **连接遥控器**, then manually enable enhanced keys and calibrate again. Voice disconnect/reconnect alone does not release the tray-owned Home/Menu mappings.

## Local development

Requirements: Windows 11 24H2+, .NET SDK `9.0.317` (see `global.json`), Bluetooth LE, the tested remote, and VB-CABLE. Enhanced keys also require the complete helper distribution beside the tray application and Windows administrator approval at enable time.

```powershell
dotnet restore src/MiVibe.Remote.Tray/MiVibe.Remote.Tray.csproj
dotnet build src/MiVibe.Remote.Tray/MiVibe.Remote.Tray.csproj --configuration Release
dotnet run --project src/MiVibe.Remote.Tray/MiVibe.Remote.Tray.csproj -- --show-status-window
```

## Release build

Build the enhanced-key helper first. The packaging script requires that output, publishes the two .NET processes as self-contained `win-x64` applications, bundles the helper, creates a portable ZIP, compiles an Inno Setup installer, and writes SHA-256 checksums.

```powershell
.\tools\Build-KeyBridge.ps1
.\tools\Publish-Release.ps1
```

The current hardware preview is 0.3.0-alpha.3. Packaging produces local artifacts; publishing the matching tag and GitHub pre-release is a separate step.

The installer compiler is intentionally not committed. Install the current Inno Setup locally or pass its `ISCC.exe` path to the script.

The 0.3 package includes `KeyBridge/MiVibe.Remote.KeyBridge.exe` and its accompanying runtime files. A normal tray-only source build can display the new UI but cannot enable keys without that packaged component. Keep helper dependencies and build outputs in ignored local directories; include their applicable notices in distributions.

## 0.3 integration and acceptance

The implementation adds Back → single Backspace, Menu → Typeless Translate (Right Shift + T), and Volume → adjacent Codex conversations. Home remains single Delete, and TV remains original backtick input. Native Home/Menu interception and the pre-existing system-wide scancode map retain their documented scope.

The revised WinForms control center retains the user-owned remote illustration and supports dark/light themes, DPI scaling, and high contrast. It displays key lifecycle state separately from microphone connectivity and guides the three calibration steps. It never fills in an invented battery value or reports enhanced keys active before the helper reaches that state.

Validation status at this documentation update:

- Independent, time-limited live tests passed for Menu opening Translate, Back single Delete including long hold, and Volume switching adjacent conversations. These tested the individual actions, not the final 0.3 package.
- The UI and its state contract compiled with 0 warnings and 0 errors.
- The alpha.2 main application has also passed user tests for Activity-view adjacent-task navigation, Back/Delete, and Menu/Translate. Its safe exit and handover from alpha.1 were exercised. See the validation record for current installation results and remaining lifecycle checks.

See [0.3 release notes](RELEASE_NOTES_0.3.0-alpha.3.md) and [live diagnostic evidence](HID_DIAGNOSTIC_2026-09-05.md).

## Preview release checks

- Build, packaging, and checksum verification pass.
- Publish unsigned hardware previews only as GitHub pre-releases with a SmartScreen warning.

## Stable release gates

- Install and launch on a clean Windows 11 24H2 x64 machine with no preinstalled .NET.
- Verify missing-dependency diagnostics, BLE reconnect, audio capture, Typeless and Translate, enhanced-key activation/calibration/reconnect, startup toggle, upgrade, and uninstall.
- Before a stable release: broaden device discovery, remove or safely replace the global scancode mapping, migrate to .NET 10 LTS, and sign the application and installer.

Detailed hardware and protocol experiments in this repository are historical engineering references, not end-user instructions.
