# MiVibe Remote 0.3 Quick Start

This preview is designed for Windows 11 x64 24H2+ and the tested Xiaomi Bluetooth Voice Remote 2 Pro. A Type-C port is not required on the PC; the remote communicates through Bluetooth Low Energy.

These instructions describe the 0.3.0-alpha.3 hardware preview. Back now sends Backspace; Home retains Delete. Menu/Translate and adjacent-task navigation in Codex Activity view retain their previously tested behavior. Other hardware and remaining lifecycle checks are still pending. A previously released 0.2 package does not contain the enhanced-key component.

## 1. Install the dependencies

1. Pair the remote in **Settings → Bluetooth & devices**.
2. Install [VB-CABLE](https://vb-audio.com/Cable/) from its official site and restart Windows if its installer requests it.
3. Open the classic Windows Sound control panel. Under **Recording**, set **CABLE Output** as both the default device and default communications device.
4. Install and sign in to Typeless and/or Codex.

VB-CABLE, Typeless, and Codex are separate products. MiVibe does not install, redistribute, or configure them silently.

## 2. Configure shortcuts

- Typeless voice input: `Numpad Divide`.
- Typeless Translate: **Right Shift + T**. Menu now starts translation.
- Codex task navigation needs no new shortcut configuration. In activity view, Volume + selects the previous loaded task row and Volume − the next, following the displayed list order. Ordinary views retain the default `Ctrl + PageUp` / `Ctrl + PageDown` behavior.

For activity view, open the task list you want to navigate and close any task menu before pressing Volume. MiVibe selects the adjacent row from the current loaded list. It stops at the first or last loaded row without wrapping and does not fetch more tasks. If a menu is open or the current task cannot be identified unambiguously, it skips the action instead of guessing. This uses the ordering shown in activity view, not visit-history back/forward or an assumed global sidebar order.

In ordinary views, MiVibe sends the default Ctrl + PageUp/PageDown shortcut. If Codex uses it to switch a side-panel tab, click the composer first, then press Volume. MiVibe does not change Codex settings.

## 3. Install MiVibe

1. Download `MiVibe-Remote-Setup-0.3.0-alpha.3-win-x64.exe` and its matching checksum file, or the complete portable ZIP, from [GitHub Releases](https://github.com/Fanatical-Naturalist/MiVibe-Remote/releases/tag/v0.3.0-alpha.3). Safely exit any running MiVibe version before upgrading; Setup keeps the existing installation directory.
2. Verify the SHA-256 checksum.
3. Run Setup and approve the administrator prompt. MiVibe is installed for all users under Program Files. This preview is unsigned, so SmartScreen may require **More info → Run anyway**.
4. Select **Apply the required remote key mapping** only after reading the warning. It needs administrator approval and a Windows restart.
5. Restart Windows, then open **MiVibe Remote** from the Start menu.
6. Optionally enable **开机自动启动** in the control center. To pin the app, find **MiVibe Remote** in the Start menu, right-click it, and select **Pin to Start**. Opening the Start-menu shortcut displays the control center; sign-in startup runs in the tray.

The mapping is system-wide: every physical `F5` becomes `F13`, and any keyboard key that emits scan code `E0 5E` (normally Power) becomes `Numpad Divide`. The installer refuses to overwrite an unrelated existing scancode map.

If you did not select the setup task, use **MiVibe Remote → Enable Remote Key Mapping** from the Start menu and restart Windows.

For the portable package, extract the entire archive and keep its `KeyBridge` folder beside the app. It does not run Setup or create Start-menu shortcuts. On a new computer, apply `tools/keyboard-remap/Enable-SplitVoiceRemap.ps1` as administrator and restart Windows before launching `MiVibe.Remote.Tray.exe`. If the MiVibe key mapping is already configured, do not apply it again.

## 4. Enable and calibrate enhanced keys

1. Double-click the MiVibe tray icon to open the control center.
2. Under **增强按键**, select **启用增强按键**. Windows requests administrator permission for the enhanced-key component. Cancelling leaves these keys disabled; it does not prevent voice input.
3. Wait for **等待按键**. Press and release **Back**, then **Volume +**, then **Volume −**, following the highlighted step. During calibration, these presses do not delete text or switch tasks.
4. Wait for **已启用** before using the mappings. Back sends one Backspace per press, including a long hold. Volume navigation takes effect only while Codex is the foreground application.
5. To repeat identification, select **重新校准**. To end only the enhanced-key session, select **停用增强按键**.

Enable enhanced keys manually each time MiVibe starts. **Start when I sign in** starts MiVibe, but does not automatically approve administrator permission or enable enhanced keys. If the driver host changes or reconnects, MiVibe tries to recover the connection and requires the three-key calibration again before sending actions.

**暂停遥控器** stops voice, enhanced keys, and the app's Home/Menu mappings. To resume, select **连接遥控器**, then manually enable enhanced keys and complete the three-key calibration again. Enhanced-key activation remains disabled while the remote is paused. This differs from automatic driver-host recovery, which asks for calibration again without another manual enable action.

The control center distinguishes disabled, starting, waiting for keys, active, reconnecting, error, and stopping states. Back and Volume are shown as unavailable until the component is active. Unknown battery readings are shown as **—**; a retained value is labeled as the last reading.

## 5. Use it

- Typeless: tap Power, hold Microphone while speaking, release it, then tap Power again when the session is complete.
- Typeless Translate: tap Menu, hold Microphone while speaking, release it, then tap Power to complete input. The translation entry shortcut passed the individual live test; complete translation output still needs regression testing with the integrated package.
- Home sends Delete, removing the character after the caret. Back sends Backspace, removing the character before the caret; enhanced keys are required. Both fire once per press, including a long hold.
- Volume + / − navigates Codex tasks while Codex is foreground: activity view follows the current loaded list, and ordinary views keep default task navigation. Close task menus first. It does not intentionally change system volume.
- TV keeps its original backtick input. MiVibe does not remap it in 0.3.
- Double-click the tray icon to view battery, connection state, audio diagnostics, reconnect, pause, or safely exit.
- Closing the window leaves MiVibe in the tray. Use **安全退出** to end the app and its enhanced-key session.

## 6. Uninstall safely

1. Choose **Safe Exit** in MiVibe.
2. Uninstall MiVibe from Windows Settings. If an exact MiVibe-managed key map is present, the uninstaller requests administrator approval and removes only that map.
3. Restart Windows to restore the original keys.

Uninstalling MiVibe does not remove VB-CABLE, Typeless, Codex, or Bluetooth pairing.

## Troubleshooting

- **Remote not found:** confirm it is paired and appears as `小米蓝牙语音遥控器` or `MI RC`.
- **No speech reaches Typeless/Codex:** set `CABLE Output` as both default recording roles, then use **Check Audio Route** in MiVibe.
- **AirPods switch to low-quality hands-free audio:** ensure the AirPods microphone is not a default recording device; leave `CABLE Output` selected for input.
- **Menu does not open Translate:** check Typeless's translation shortcut and test **Right Shift + T** manually.
- **Back and Volume do nothing:** if the remote is paused, select **连接遥控器** first. Then enable enhanced keys and complete all three press/release calibration steps; wait for **已启用**. Restarting MiVibe also requires manual enable and calibration. After automatic driver-host recovery, follow the calibration prompts again.
- **Volume does not switch tasks:** put Codex in the foreground and confirm enhanced keys are active. In activity view, close the task menu and make sure the current task is identifiable in the loaded list. Reaching the list boundary or an ambiguous selection intentionally does nothing. In ordinary views, click the composer and check that the default Ctrl + PageUp/PageDown shortcuts work. No new binding is needed.
- **Enhanced-key component missing:** use the full 0.3 package with its `KeyBridge` folder; copying the tray executable alone is insufficient.
- **Enhanced keys are reconnecting:** wake the paired remote and wait for recovery, then follow calibration again. If the component shows an error, retry from the control center. Voice and key connections are displayed separately.
- **Keys are still wrong after mapping:** restart Windows. The scancode map is applied only at boot.
- **A long hold stops:** release and hold Microphone again inside the same Typeless session.
