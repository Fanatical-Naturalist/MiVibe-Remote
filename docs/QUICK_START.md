# MiVibe Remote 0.2 Quick Start

This preview is designed for Windows 11 x64 24H2+ and the tested Xiaomi Bluetooth Voice Remote 2 Pro. A Type-C port is not required on the PC; the remote communicates through Bluetooth Low Energy.

## 1. Install the dependencies

1. Pair the remote in **Settings → Bluetooth & devices**.
2. Install [VB-CABLE](https://vb-audio.com/Cable/) from its official site and restart Windows if its installer requests it.
3. Open the classic Windows Sound control panel. Under **Recording**, set **CABLE Output** as both the default device and default communications device.
4. Install and sign in to Typeless and/or Codex.

VB-CABLE, Typeless, and Codex are separate products. MiVibe does not install, redistribute, or configure them silently.

## 2. Configure shortcuts

- Typeless voice input: `Numpad Divide`.
- Codex Voice: record `Ctrl + Alt + Shift + 8`. Codex may normalize the label to `Ctrl + Alt + *`.

## 3. Install MiVibe

1. Download `MiVibe-Remote-Setup-0.2.0-alpha.1-win-x64.exe` and the checksum file from the GitHub release.
2. Verify the SHA-256 checksum.
3. Run Setup and approve the administrator prompt. MiVibe is installed for all users under Program Files. This preview is unsigned, so SmartScreen may require **More info → Run anyway**.
4. Select **Apply the required remote key mapping** only after reading the warning. It needs administrator approval and a Windows restart.
5. Restart Windows, then open **MiVibe Remote** from the Start menu.
6. Optionally enable **Start when I sign in** from the MiVibe tray window.

The mapping is system-wide: every physical `F5` becomes `F13`, and any keyboard key that emits scan code `E0 5E` (normally Power) becomes `Numpad Divide`. The installer refuses to overwrite an unrelated existing scancode map.

If you did not select the setup task, use **MiVibe Remote → Enable Remote Key Mapping** from the Start menu and restart Windows.

## 4. Use it

- Typeless: tap Power, hold Microphone while speaking, release it, then tap Power again when the session is complete.
- Codex Voice: tap Menu, hold Microphone while speaking, and release it to finish the segment. Tap Menu again to close Voice.
- Double-click the tray icon to view battery, connection state, audio diagnostics, reconnect, pause, or safely exit.

## 5. Uninstall safely

1. Choose **Safe Exit** in MiVibe.
2. Uninstall MiVibe from Windows Settings. If an exact MiVibe-managed key map is present, the uninstaller requests administrator approval and removes only that map.
3. Restart Windows to restore the original keys.

Uninstalling MiVibe does not remove VB-CABLE, Typeless, Codex, or Bluetooth pairing.

## Troubleshooting

- **Remote not found:** confirm it is paired and appears as `小米蓝牙语音遥控器` or `MI RC`.
- **No speech reaches Typeless/Codex:** set `CABLE Output` as both default recording roles, then use **Check Audio Route** in MiVibe.
- **AirPods switch to low-quality hands-free audio:** ensure the AirPods microphone is not a default recording device; leave `CABLE Output` selected for input.
- **Menu does not open Codex Voice:** test `Ctrl + Alt + Shift + 8` manually. This preview relies on Codex normalizing that tested sequence to `Ctrl + Alt + *`, so other keyboard layouts may need a different binding in a future release.
- **Keys are still wrong after mapping:** restart Windows. The scancode map is applied only at boot.
- **A long hold stops:** release and hold Microphone again inside the same Typeless session.
