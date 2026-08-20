# F5 → F13 可逆实验

> 实验结论：失败。Typeless 2.3.1 for Windows 没有识别系统映射后的 F13。本文件保留为过程记录；当前实验已改为 `F5 → Numpad Divide`。

## 目标

把 Windows 收到的实体 `F5` 扫描码全局转换为 `F13`，使 Typeless 将小米遥控器麦克风键视为一枚没有常见系统副作用的实体键。

这不是按设备映射：电脑键盘上的实体 `F5` 也会变为 `F13`。启用和撤销都需要重启 Windows。

## 为什么选择 F13

- Typeless 2.3.1 的键名表明确支持 `F13`。
- `F13` 几乎没有 Windows 默认行为。
- `PrtSc` 通常触发截图，小键盘键可能影响数字输入。
- 系统扫描码映射发生在普通应用接收按键之前，不依赖已被 Typeless 拒绝的 `SendInput` 模拟输入。

## 启用

1. 以管理员身份打开 PowerShell。
2. 进入项目目录。
3. 运行：

   ```powershell
   .\tools\keyboard-remap\Enable-F5ToF13.ps1
   ```

4. 确认脚本显示写入和回读验证成功。
5. 重启 Windows。

脚本发现任何其他 `Scancode Map` 时会停止，不会覆盖已有映射。

## 重启后的验证顺序

1. 打开 Typeless → 设置 → 键盘快捷键 → 语音输入 → 添加另一个。
2. 短按遥控器麦克风键，确认 Typeless 显示 `F13`，并且不再显示“此快捷键已保留供系统使用”。
3. 暂时不要删除原有的右 `Ctrl + 右 Shift`，它仍是键盘兜底入口。
4. 在记事本中单独按遥控器麦克风键，确认不再插入日期和时间。
5. 运行现有 `--voice-live` 音频桥，按住遥控器麦克风键说话，然后松开；确认 Typeless 自动开始、接收 VB-CABLE 音频并停止。
6. 检查电脑实体键盘 F5，确认它现在同样表现为 F13。这是该方案的已知全局代价。

## 查看状态

普通 PowerShell 可运行：

```powershell
.\tools\keyboard-remap\Get-F5ToF13Status.ps1
```

## 撤销

1. 以管理员身份打开 PowerShell。
2. 在项目目录运行：

   ```powershell
   .\tools\keyboard-remap\Disable-F5ToF13.ps1
   ```

3. 重启 Windows，实体 F5 恢复正常。

撤销脚本只会删除与本实验完全一致的映射；如果发现其他值，它会停止并保留现状。
