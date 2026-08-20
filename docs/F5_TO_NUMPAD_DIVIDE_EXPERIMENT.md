# F5 → Numpad Divide 可逆实验

> 结果：Typeless 键位识别成功，但长按麦克风会打开快捷键帮助层，端到端交互失败。本实验已由 `SPLIT_VOICE_REMAP.md` 中的双按键映射取代。

## 目标

把 Windows 收到的实体 `F5` 全局转换为小键盘除法键 `Numpad Divide`，并将它设置为 Typeless 的第二套语音输入快捷键。

电脑键盘上的实体 F5 也会变成 Numpad Divide；启用和撤销均需重启 Windows。

## 已完成的预检

- Typeless 能识别 Numpad 系列实体键。
- 用户选择 Numpad Divide，因为它比乘号使用频率更低。
- Numpad Divide 的 Windows 扩展扫描码为 `E0 35`，不受 NumLock 状态影响。
- 原 F13 实验失败；迁移工具只允许替换该实验的精确映射值。

## 历史工具状态

本实验对应的启用、状态和撤销脚本已经由分离式语音控制工具取代，不应再按本页执行旧映射。当前可运行的命令和回滚方式统一记录在 `SPLIT_VOICE_REMAP.md`。

## 重启后的测试

1. 在 Typeless → 设置 → 键盘快捷键 → 语音输入中保留右 `Ctrl + 右 Shift`。
2. 通过“添加另一个”保存 `Numpad Divide`。
3. 按遥控器麦克风键，确认 Typeless 能开始和停止语音输入。
4. 运行 MiVibe `--voice-live`，确认遥控器音频通过 VB-CABLE 被 Typeless 转写。
5. 确认记事本不再插入日期和时间。

## 状态与撤销

请使用 `SPLIT_VOICE_REMAP.md` 中的 `Get-VoiceRemapStatus.ps1` 和 `Disable-VoiceRemap.ps1`。撤销后仍需重启 Windows。
