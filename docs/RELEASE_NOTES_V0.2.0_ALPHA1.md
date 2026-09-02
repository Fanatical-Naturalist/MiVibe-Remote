# MiVibe Remote 0.2.0-alpha.1

The first public hardware preview packages the tested Xiaomi remote workflow as a Windows tray application.

## Highlights

- Typeless and Codex Voice remote workflows.
- BLE microphone audio bridged to VB-CABLE.
- Connection and battery status window.
- Automatic bridge reconnect, pause/resume, audio diagnostics, safe exit, and optional start-on-sign-in.
- Self-contained Windows x64 build: no separate .NET installation is required.
- Setup EXE, portable ZIP, and SHA-256 checksums. Setup is recommended; the ZIP is for advanced users.

## Before installing

Read the [Quick Start](https://github.com/Fanatical-Naturalist/MiVibe-Remote/blob/v0.2.0-alpha.1/docs/QUICK_START.md). Windows 11 x64 24H2+, Bluetooth LE, VB-CABLE, Typeless/Codex configuration, administrator approval for the per-machine install and key mapping, and one restart are required.

The portable ZIP does not create shortcuts or apply the required mapping. Advanced users must run `tools/keyboard-remap/Enable-SplitVoiceRemap.ps1` as administrator and restart Windows.

This build is unsigned and verified on one remote (`VID 2717 / PID 32B8`). It is a GitHub pre-release, not a universal or plug-and-play remote driver.

## Known limitations

- Device discovery currently recognizes only `小米蓝牙语音遥控器` and `MI RC`.
- The key mapping is system-wide: every physical `F5` becomes `F13`, and any key that emits scan code `E0 5E` becomes `Numpad Divide`.
- Physical computer `Menu/Application` and `Home` keys are intercepted while MiVibe is active.
- Remote Back and Volume buttons are not available through the current user-mode path.
- On the tested remote, a continuous microphone hold may stop at about 60 seconds; use multiple holds within one Typeless session.
- The Codex Voice shortcut uses the tested `Ctrl + Alt + Shift + 8` sequence, normalized by Codex to `Ctrl + Alt + *`; other keyboard layouts may require a future configurable binding.
