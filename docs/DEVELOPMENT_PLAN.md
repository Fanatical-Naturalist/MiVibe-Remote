# Development overview

MiVibe Remote is a Windows 11 WinForms tray application plus a separate BLE/audio bridge process.

## Architecture

- `MiVibe.Remote.Tray` owns the tray icon, status window, battery display, startup preference, and reconnect policy.
- `MiVibe.Remote.GattProbe` connects to the paired BLE remote, negotiates ATVV 1.x, decodes microphone audio, and renders PCM to VB-CABLE.
- The release keeps the bridge as a separate process so it can be stopped safely and restarted after a disconnect.

## Local development

Requirements: Windows 11 24H2+, .NET 9 SDK, Bluetooth LE, the tested remote, and VB-CABLE.

```powershell
dotnet restore src/MiVibe.Remote.Tray/MiVibe.Remote.Tray.csproj
dotnet build src/MiVibe.Remote.Tray/MiVibe.Remote.Tray.csproj --configuration Release
dotnet run --project src/MiVibe.Remote.Tray/MiVibe.Remote.Tray.csproj -- --show-status-window
```

## Release build

The packaging script publishes both processes as self-contained `win-x64` applications, creates a portable ZIP, compiles an Inno Setup installer, and writes SHA-256 checksums.

```powershell
.\tools\Publish-Release.ps1
```

The installer compiler is intentionally not committed. Install the current Inno Setup locally or pass its `ISCC.exe` path to the script.

## Preview release checks

- Build, packaging, and checksum verification pass.
- Publish unsigned hardware previews only as GitHub pre-releases with a SmartScreen warning.

## Stable release gates

- Install and launch on a clean Windows 11 24H2 x64 machine with no preinstalled .NET.
- Verify missing-dependency diagnostics, BLE reconnect, audio capture, Typeless, Codex Voice, startup toggle, upgrade, and uninstall.
- Before a stable release: broaden device discovery, remove or safely replace the global scancode mapping, migrate to .NET 10 LTS, and sign the application and installer.

Detailed hardware and protocol experiments in this repository are historical engineering references, not end-user instructions.
