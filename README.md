# MiVibe Remote

English | [简体中文](README.zh-CN.md)

Turn a Xiaomi Bluetooth voice remote into a hands-on voice input controller for Windows 11, Typeless, and Codex Voice.

![MiVibe Remote key guide](docs/assets/mivibe-remote-user-guide-dark.png)

> **0.2 hardware preview.** This project has been verified with one Xiaomi Bluetooth Voice Remote 2 Pro (`VID 2717 / PID 32B8`). It is not a universal Bluetooth remote driver.

## Features

- Streams the remote microphone to Windows through VB-CABLE.
- Toggles Typeless from the remote Power button.
- Opens Codex Voice from the remote Menu button.
- Shows connection state and battery level in a tray window.
- Reconnects the voice bridge after an unexpected disconnect.
- Supports optional start-on-sign-in and checks the Windows VB-CABLE input / AirPods output configuration. MiVibe does not switch audio devices automatically.

## Requirements

- Windows 11 x64, version 24H2 or later.
- A PC with Bluetooth Low Energy. USB Type-C is only used to charge the remote; it is not a PC requirement.
- A compatible Xiaomi remote paired in Windows as `小米蓝牙语音遥控器` or `MI RC`.
- [VB-CABLE](https://vb-audio.com/Cable/) installed separately.
- Typeless and/or Codex installed and signed in.

## Quick start

1. Download the latest **Setup EXE** from [Releases](https://github.com/Fanatical-Naturalist/MiVibe-Remote/releases). Setup installs for all users under Program Files and requests administrator approval.
2. Pair the remote in Windows Bluetooth settings and install VB-CABLE.
3. Set **CABLE Output** as both the default recording device and the default communications recording device.
4. Configure Typeless as `Numpad Divide`. For Codex Voice, record `Ctrl + Alt + Shift + 8`; Codex may display the normalized shortcut as `Ctrl + Alt + *`.
5. Apply **Remote Key Mapping** during Setup (or from the Start menu), restart Windows, then launch MiVibe Remote. Start-on-sign-in can be enabled in the tray window.

See [Quick Start](docs/QUICK_START.md) for the exact setup and uninstall steps.

## Controls

| Remote control | Action |
| --- | --- |
| Power, tap | Start or finish a Typeless session |
| Microphone, hold | Speak; release to finish the current segment |
| Direction ring / center | Navigation / Enter |
| Home | Delete the character after the caret |
| Menu | Open or close Codex Voice |
| TV | Preserved original input; no MiVibe action |
| Back / Volume | Not available from the current Windows user-mode path |

## Important limitations

- The required key mapping is system-wide: every physical `F5` becomes `F13`, and any keyboard key that emits scan code `E0 5E` (normally Power) becomes `Numpad Divide` while it is enabled.
- MiVibe also intercepts physical computer `Menu/Application` and `Home` keys while the voice bridge is running. Pause or safely exit MiVibe to restore them.
- On the tested remote, a continuous microphone hold may stop at about 60 seconds. Release and hold again inside the same Typeless session.
- The Codex Voice shortcut is layout-sensitive in this preview. Test `Ctrl + Alt + Shift + 8` manually if Menu does not open Voice.
- VB-CABLE, Typeless, and Codex are third-party products and are not bundled with MiVibe.
- The current installer is unsigned, so Windows SmartScreen may show an unknown-publisher warning. Verify the published SHA-256 checksum before running it.
- Uninstall removes only an exact MiVibe-managed key map and then requests a Windows restart; unrelated maps are never changed.

## Build from source

```powershell
dotnet build src/MiVibe.Remote.Tray/MiVibe.Remote.Tray.csproj --configuration Release
```

Release packaging requires the current .NET 9 SDK and Inno Setup. Contributor notes are in [Development](docs/DEVELOPMENT_PLAN.md). Historical protocol experiments remain in `docs/` for reference.

## License

MiVibe Remote is released under the [MIT License](LICENSE). Third-party components keep their own licenses; see [Third-Party Notices](THIRD-PARTY-NOTICES.md).
