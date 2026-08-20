# BLE GATT 服务记录

测试设备：小米蓝牙语音遥控器，`VID 0x2717 / PID 0x32B8`
采样日期：2026-08-19
隐私约束：设备唯一蓝牙地址不写入仓库。

## 枚举结果

Windows 应用层成功发现 9 个服务。标准 HID 服务 `0x1812` 由系统 HID 驱动占用，characteristic 枚举返回 `AccessDenied`；其他服务可以访问。

| Service | 主要 characteristic | 属性 | 当前判断 |
|---|---|---|---|
| `000001BF-...` | `00000001-...` | Write Without Response | 未知厂商配置，仅记录，不写入 |
| `0x1800` | GAP 标准 characteristic | Read | 标准服务 |
| `0x1801` | Service Changed | Indicate | 标准服务，不监听 |
| `0x180A` | Device Information | Read | 标准服务 |
| `0x180F` | Battery Level 等 | Read/Notify | 标准服务，不用于按键实验 |
| `0x1812` | HID | AccessDenied | Windows 系统驱动占用 |
| `0xFE59` | DFU characteristic | Write/Notify | 固件升级相关，实验中排除 |
| `8A7A0001-...` | `0102`、`0103`、`0112` | Notify | 小米厂商自定义，优先观察返回/音量事件 |
| `AB5E0001-...` | `0003`、`0004` | Notify | Android TV Voice（ATVV）语音与控制通道 |

## 安全边界

- `--list` 只枚举结构，不修改 descriptor 或 characteristic。
- `--listen` 只允许订阅 `8A7A...` 和 `AB5E...` 的 Notify characteristic。
- 不订阅标准系统服务或 `0xFE59` 固件升级服务。
- 不向任何厂商 characteristic 写入数据。
- 通知模式只设置标准 Client Characteristic Configuration Descriptor（CCCD），结束时恢复为 `None`。

## 当前实验目标

1. 订阅 5 个允许的 Notify characteristic。
2. 依次多次按返回、音量加、音量减。
3. 用 Home 和麦克风作为已知对照键。
4. 若 `8A7A...` 有稳定通知，比较字节差异并建立映射。
5. 若只有 `AB5E...` 在麦克风键动作时变化，确认 ATVV 控制边界，再研究握手和音频编码。
6. 若三个缺失按键在 GATT 层仍无通知，下一步直接读取 HID report descriptor/原始 report。

## ATVV 语音路线更新（2026-08-20）

- 公开的遥控器手册与 Android TV 参考遥控器源码均表明：主机必须先向 `AB5E0002...` 写入能力查询，再通过控制通知完成协商。
- 主机发送 `MIC_OPEN` 后，遥控器才会启动语音采集，并通过 `AB5E0003...` 发送音频通知；因此此前只订阅通知但没有数据，不能判定麦克风不可用。
- 参考实现包含 8 kHz 与 16 kHz ADPCM 路径。具体编码、帧长和控制字节必须以本设备的能力响应为准，不能直接硬编码猜测。
- 下一次实验会从“只读订阅”升级为“仅写入公开 ATVV 控制命令”，仍不触碰 DFU/固件服务。

## 第一版 ATVV 1.0 采音探针

- 新增显式命令 `--voice-capture [seconds]`；普通 `--list` 和 `--listen` 行为不变。
- 能力查询固定为 ATVV 1.0 规范定义的 `0A 01 00 00 03 00`。
- 只在收到合法 `CAPS_RESP` 且 major version 为 1 后发送 `MIC_OPEN`；其他版本停止，不猜测旧协议。
- 订阅 `ATVV_CHAR_AUDIO` 与 `ATVV_CHAR_CTL` 后才开麦；结束时发送 `MIC_CLOSE` 并取消订阅。
- 同时输出原始 `.adpcm` 与单声道 16-bit `.wav`，便于解码问题复核。
- 不访问 `0xFE59` DFU 服务，不写 HID、8A7A 或其他厂商 characteristic。
