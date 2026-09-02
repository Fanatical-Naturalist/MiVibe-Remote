# Changelog

## 0.2.0-alpha.1 — 2026-09-02

First public hardware preview.

### Added

- Windows tray app with connection, battery, reconnect, pause, audio diagnostics, start-on-sign-in, and safe exit controls.
- Typeless workflow on remote Power and Codex Voice workflow on remote Menu.
- Home-to-Delete mapping and direction-ring navigation.
- Automatic BLE/audio bridge restart after an unexpected disconnect.
- Dark/light status window and remote key guide.
- Self-contained Windows x64 Setup EXE, portable ZIP, and SHA-256 checksums.

### Changed

- Release logs now live in `%LOCALAPPDATA%\MiVibe Remote\logs`.
- The tray launches the self-contained bridge executable directly; a separate .NET installation is no longer required.

### Known limitations

- Verified with one Xiaomi remote (`VID 2717 / PID 32B8`) on Windows 11 x64 24H2.
- VB-CABLE, Typeless/Codex shortcut setup, a system-wide key mapping, and one Windows restart are still required.
- Physical keyboard `F5`, Power (`E0 5E`), `Menu/Application`, and `Home` have documented global conflicts while the corresponding mapping or hook is active.
- Remote Back and Volume buttons are unavailable through the current user-mode path.
- The preview installer is not code-signed.

## 0.1.0-prototype — 2026-08-21

First locally frozen prototype of the BLE ATVV capture, VB-CABLE audio path, Typeless control, Codex Voice control, and tray host.
