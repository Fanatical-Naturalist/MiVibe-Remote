# MiVibe Remote

English | [简体中文](README.zh-CN.md)

Turn a Xiaomi Bluetooth voice remote into a controller for voice input, Typeless Translate, and adjacent Codex tasks on Windows 11.

> **0.3.0-alpha.3 hardware preview.** Back now sends **Backspace** to remove the character before the caret; Home keeps **Delete** for the character after it. Menu opens Typeless Translate, and Volume follows adjacent tasks in Codex Activity view. Tested hardware: Xiaomi Bluetooth Voice Remote 2 Pro (`VID 2717 / PID 32B8 / REV 00A4`). Compatibility with other remotes and remaining lifecycle tests are not yet confirmed.

**[Download 0.3 for Windows x64](https://github.com/Fanatical-Naturalist/MiVibe-Remote/releases/tag/v0.3.0-alpha.3)** · [What's new](docs/RELEASE_NOTES_0.3.0-alpha.3.md)

## Features

- Streams the remote microphone to Windows through VB-CABLE.
- Toggles Typeless from the remote Power button.
- Opens Typeless Translate from the remote Menu button.
- Adds optional Back → Backspace and Volume → previous/next Codex task controls. Activity-view navigation follows the loaded list order.
- Shows device, battery, and enhanced-key connection state in a refreshed tray control center with light and dark themes.
- Reconnects the voice bridge after an unexpected disconnect.
- Supports optional start-on-sign-in and checks the Windows VB-CABLE input / AirPods output configuration. MiVibe does not switch audio devices automatically.

## Requirements

- Windows 11 x64, version 24H2 or later.
- A PC with Bluetooth Low Energy. USB Type-C is only used to charge the remote; it is not a PC requirement.
- A compatible Xiaomi remote paired in Windows as `小米蓝牙语音遥控器` or `MI RC`.
- [VB-CABLE](https://vb-audio.com/Cable/) installed separately.
- Typeless and/or Codex installed and signed in.

## Quick start

1. Download the **0.3.0-alpha.3 Setup EXE** or complete portable package from [Releases](https://github.com/Fanatical-Naturalist/MiVibe-Remote/releases/tag/v0.3.0-alpha.3). When upgrading, safely exit MiVibe first. Setup keeps the existing installation directory and requests administrator approval. Older 0.2 packages do not include enhanced keys.
2. Pair the remote in Windows Bluetooth settings and install VB-CABLE.
3. Set **CABLE Output** as both the default recording device and the default communications recording device.
4. Configure Typeless voice input as `Numpad Divide` and Typeless Translate as **Right Shift + T**. Volume navigation needs no new Codex shortcut: activity view follows the loaded task list; ordinary views keep the default `Ctrl + PageUp` / `Ctrl + PageDown` behavior.
5. Apply **Remote Key Mapping** during Setup (or from the Start menu), restart Windows, then launch MiVibe Remote. Start-on-sign-in can be enabled in the tray window.
6. Double-click the tray icon, select **启用增强按键**, and approve the Windows administrator prompt. Follow the three steps: press and release **Back → Volume + → Volume −**. Calibration does not trigger Delete or task navigation.

Enhanced keys must be enabled manually after each app launch, including start-on-sign-in. After a driver-host reconnect, repeat the three-key calibration. Closing the control-center window keeps the app running; **停用增强按键** stops the enhanced keys, and **安全退出** ends the app.

**暂停遥控器** stops voice, enhanced keys, and the app's Home/Menu mappings. To resume, select **连接遥控器** first, then enable enhanced keys again and complete calibration. The enhanced-key enable button is unavailable while the remote is paused.

See [Quick Start](docs/QUICK_START.md) for the exact setup and uninstall steps.

## Controls

| Remote control | Action |
| --- | --- |
| Power, tap | Start or finish a Typeless session |
| Microphone, hold | Speak; release to finish the current segment |
| Direction ring / center | Navigation / Enter |
| Home | Delete once, including on a long hold |
| Menu | Open Typeless Translate with Right Shift + T |
| Back | Backspace once, including on a long hold; enhanced keys required |
| Volume + / − | Activity view: previous / next loaded task row in displayed order; ordinary views: default task navigation. Requires enhanced keys and Codex in foreground |
| TV | Preserved original backtick input; no MiVibe action |

## Important limitations

- The required key mapping is system-wide: every physical `F5` becomes `F13`, and any keyboard key that emits scan code `E0 5E` (normally Power) becomes `Numpad Divide` while it is enabled.
- MiVibe also intercepts physical computer `Menu/Application` and `Home` keys while the app is not paused, including while voice is disconnected or reconnecting. Pause or safely exit MiVibe to restore them.
- On the tested remote, a continuous microphone hold may stop at about 60 seconds. Release and hold again inside the same Typeless session.
- The enhanced-key component needs administrator permission, supports the tested hardware family, and handles the synchronous report path observed on that remote. Driver-host recovery requires calibration again. It does not install a HID filter driver or change the TV key.
- Activity-view navigation stops at the first or last loaded row without wrapping. Close task menus first; if a menu is open or the current task cannot be identified unambiguously, MiVibe skips the action. It does not load additional tasks. Ordinary views keep `Ctrl + PageUp` / `Ctrl + PageDown`; focus the composer if a side panel consumes those shortcuts. No new Codex binding is required.
- Enhanced keys do not install global suppression for native Back/Volume keyboard events. Other firmware or Windows versions may need further compatibility work; final 0.3 coexistence and reconnect tests remain pending.
- VB-CABLE, Typeless, and Codex are third-party products and are not bundled with MiVibe.
- The current installer is unsigned, so Windows SmartScreen may show an unknown-publisher warning. Verify the published SHA-256 checksum before running it.
- Uninstall removes only an exact MiVibe-managed key map and then requests a Windows restart; unrelated maps are never changed.

## Build from source

```powershell
dotnet build src/MiVibe.Remote.Tray/MiVibe.Remote.Tray.csproj --configuration Release
```

Release packaging requires .NET SDK `9.0.317` and Inno Setup. Run `tools/Build-KeyBridge.ps1` to package the enhanced-key helper before running `tools/Publish-Release.ps1`. Contributor notes are in [Development](docs/DEVELOPMENT_PLAN.md); the current scope and acceptance boundary are in [0.3 release notes](docs/RELEASE_NOTES_0.3.0-alpha.3.md). Historical protocol experiments remain in `docs/` for reference.

## License

MiVibe Remote is released under the [MIT License](LICENSE). Third-party components keep their own licenses; see [Third-Party Notices](THIRD-PARTY-NOTICES.md).
