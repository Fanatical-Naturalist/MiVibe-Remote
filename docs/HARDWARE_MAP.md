# 小米蓝牙语音遥控器：硬件按键记录

测试设备：`VID 0x2717 / PID 0x32B8`
传输：Bluetooth LE HID Keyboard
采样日期：2026-08-19

## 第一轮采样结果

| 实体按键 | Windows 事件 | Scan code | 状态 | 证据/说明 |
|---|---:|---:|---|---|
| 上 | `VK_UP 0x26` | `0x48` | 已确认 | 按下、松开成对 |
| 下 | `VK_DOWN 0x28` | `0x50` | 已确认 | 按下、松开成对 |
| 左 | `VK_LEFT 0x25` | `0x4B` | 已确认 | 按下、松开成对 |
| 右 | `VK_RIGHT 0x27` | `0x4D` | 已确认 | 按下、松开成对 |
| 确认 | `VK_RETURN 0x0D` | `0x1C` | 已确认 | 按下、松开成对 |
| 返回 | 无标准 Raw Input 事件 | — | 已确认异常 | 用户明确反馈无反应；确认键之后到 Home 之前没有事件 |
| Home | `VK_HOME 0x24` | `0x47` | 高置信推断 | 与用户指定的采样顺序一致 |
| 菜单 | `VK_APPS 0x5D` | `0x5D` | 高置信推断 | 与用户指定的采样顺序一致 |
| TV | `VK_OEM_3 0xC0` | `0x29` | 高置信推断 | Windows 键位通常是反引号键 |
| 开关 | `VK 0xFF` | `E0 5E` | 已确认 | 扩展键；按下、松开成对；轻触无系统动作 |
| 音量 + | 未观察到独立事件 | — | 待定 | 需要与音量 - 单独复测 |
| 音量 - | 未观察到独立事件 | — | 待定 | 需要与音量 + 单独复测 |
| 麦克风短按 | `VK_F5 0x74` | `0x3F` | 高置信推断 | 第一组 F5 为短按 |
| 麦克风长按 | `VK_F5 0x74` 自动重复 | `0x3F` | 高置信推断 | 约 32ms 重复一次，松开时有 key-up |

## 当前判断

返回键没有进入当前 BLE HID Keyboard 的 Raw Input 事件流。可能性按优先级排列：

1. 遥控器固件只在电视/Android 主机协商后发送返回键。
2. 返回键使用 Consumer Control、Android/vendor usage，被 Windows 翻译层消费或忽略。
3. 返回键通过厂商自定义 GATT 服务发送，而不是标准 HID Keyboard report。
4. 实体按键或当前配对模式存在硬件/固件问题。

下一轮先用增强探针同时记录 Raw Input、Windows 全局翻译键盘事件和 `WM_APPCOMMAND`。如果仍无事件，再进入自定义 GATT 通知捕获。

## 第二轮针对性采样

增强探针先后成功捕获普通键盘空格键及后续 `Ctrl+C`、`Ctrl+V` 等操作，证明 Raw Input 和全局翻译事件链路均正常。用户在两个空格标记之间分别多次按返回、音量加、音量减，三类按键都没有产生目标 Raw Input、翻译键盘事件或 `WM_APPCOMMAND`。

结论：返回、音量加和音量减不进入 Windows 普通输入栈。下一层验证改为 BLE GATT characteristic 枚举与通知捕获。

## HID descriptor 能力

Windows 已解析的目标 top-level collection：

- Usage Page/Usage：`0x0001 / 0x0006`（Generic Desktop / Keyboard）
- 最大 input report：121 字节
- output/feature report：0 字节
- Report ID `0x01`：Keyboard Page `0x0007`，usage range `0x0000–0x00FE`
- Report ID `0x06`、`0x07`、`0x08`：Vendor Page `0xFF00`，usage range `0x0000–0x00FF`

解释：音量键可能使用 Keyboard Page 的 `0x80/0x81`，它们落在 descriptor 范围内但没有被 Windows 键盘翻译层转成事件。Consumer Page 的 Back `0x0224` 没有在该 collection 的 capability 中出现。厂商 report ID 表明设备还存在 Windows 不会转换成普通键盘事件的输入数据。

共享只读打开 HID input report 流时，Windows 返回 `Access Denied (5)`。因此用户态程序只能获取 descriptor 能力，不能直接读取这个系统键盘 collection 的原始 121 字节 report。若必须支持缺失按键，需要可选的 HID filter driver；否则 v0.1 只能映射 Windows 已提供的按键事件。
