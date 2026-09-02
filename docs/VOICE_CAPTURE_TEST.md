# ATVV 麦克风首次采音测试

## 目的

验证电脑能否通过小米遥控器的 Android TV Voice（ATVV）服务主动开麦，并把收到的 ADPCM 音频还原为可听 WAV。

本测试不会启动 Typeless、不会安装 VB-CABLE，也不会访问固件升级服务。

## 隐私说明

- 执行命令后，遥控器会尝试录制 8 秒环境声音。
- 音频只写入本项目 `logs` 目录，不上传网络。
- 测试前请确认周围人员知情，并避免说出密码、令牌或其他敏感信息。
- 测试完成后，可以直接删除 WAV 和 ADPCM；它们已被 `.gitignore` 的日志规则排除，不应提交 GitHub。

## 操作步骤

1. 打开 PowerShell，进入项目目录：

   ```powershell
   cd "<MiVibe-Remote 仓库目录>"
   ```

2. 为满足遥控器的“最近有用户操作”隐私限制，先短按一次遥控器任意可用按键，例如方向键。

3. 运行 8 秒采音：

   ```powershell
   .\src\MiVibe.Remote.GattProbe\bin\Debug\net9.0-windows10.0.26100.0\MiVibe.Remote.GattProbe.exe --voice-capture 8 --out .\logs\atvv-first-voice.wav
   ```

4. 看到 `Capturing for 8 seconds` 后，对着遥控器清晰说一句：“小米遥控器语音测试，一二三。”

5. 成功时会生成：

   - `logs\atvv-first-voice.wav`
   - `logs\atvv-first-voice.adpcm`

## 请保留的诊断信息

- 完整终端输出。
- WAV 是否能播放、能否听清人声、速度是否正常。
- 若失败，记录错误码；常见 ATVV 错误包括未启用音频通知、遥控器近期未激活或已有其他语音会话。

请不要把包含私人谈话的音频提交到公开仓库。

## 第一次测试结果与复测说明

第一次测试已证明 GET_CAPS、MIC_OPEN、AUDIO_START、MIC_CLOSE 和 AUDIO_STOP 控制链路成功，但没有收到音频通知。增强版工具会额外显示 GATT PDU/MTU、CCCD 读回状态和前几个音频包。

复测仍使用相同命令。为隔离变量，开始前只短按方向键保持遥控器活跃，采音期间先不要按住实体麦克风键；如果仍无包，再单独测试实体键配合方案。

第二次测试已确认 MTU 247、两个 CCCD 均为 Notify，但仍没有音频包。第三次测试需要在看到 `Capturing` 后持续按住实体麦克风键直到采音结束，用于验证遥控器固件的物理隐私门控。

第三次测试成功：按住实体麦克风键后进入 Hold-to-Talk stream 1，收到 413 个音频包并生成约 6.195 秒的 16 kHz WAV。该设备当前需要实体键持续按住才能实际发送麦克风音频。
