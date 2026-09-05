# Changelog

## 0.3.0-alpha.2 — 2026-09-06 (hardware preview)

- Pause and safe exit revoke key actions immediately while the observation helper cleans up.
- Paused controls require resuming the remote before enhanced keys can be enabled again.
- Reconnecting replaces a failed key controller while preserving a healthy controller.
- Start-menu shortcuts show the existing control center when MiVibe is already running.
- Volume navigation in Codex Activity view follows the displayed, loaded task rows
  instead of the narrower list used by Codex's default previous/next shortcuts.
- Ordinary-view shortcut fallback submits one complete batch and releases only
  accepted keys after a partial send. Activity-list boundaries do not wrap.
- Retains alpha.1 key mappings, UI and the packaged observation helper.

## 0.3.0-alpha.1 — 2026-09-05 (local preview)

- Integrated Back → Delete, Menu → Typeless Translate, and Volume → adjacent
  Codex conversation navigation. Menu, Home and enhanced actions share one queue.
- Added an optional packaged Windows administrator helper, three-key calibration,
  driver reconnect handling, and safe shutdown through an authenticated local pipe.
- Refreshed the native control center with light/dark themes, live enhanced-key
  status, calibration steps, updated remote guide and recent action feedback.
- Built the self-contained x64 installer and portable archive. Python, JavaScript
  parser/lifecycle tests and the packaged helper's live readiness/exit checks passed.
- This is a local preview. Integrated physical-key acceptance and installation
  upgrade testing remain separate from the earlier successful individual probes.

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
