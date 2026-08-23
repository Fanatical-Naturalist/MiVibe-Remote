# MiVibe Remote

把小米蓝牙语音遥控器变成 Windows 上的 Vibe Coding 控制器。

当前冻结基线：`v0.1.0-prototype`。这是已通过本机实体测试、供日常使用的本地版本，暂未发布到 GitHub。冻结能力和限制见 [v0.1.0-prototype 说明](docs/RELEASE_V0.1.0_PROTOTYPE.md)。

当前开发线：`0.2.0-alpha.1`。冻结包不变；新代码正在验证蓝牙断线自动恢复，随后增加标准电池服务读取、发布基础和轻量设备状态窗口。

当前仓库处于硬件验证与可用原型阶段。第一版复用 Windows 自带的 Bluetooth LE HID 驱动，目标是不安装自研内核驱动即可完成：

- 精确区分遥控器与普通键盘；
- 记录每个实体按键产生的原始事件；
- 将单击、双击、长按映射为可配置快捷键或受控动作；
- 通过 ATVV 解码遥控器麦克风，并经 VB-CABLE 提供给 Typeless 等语音应用。

Typeless 分离式语音链路已完成实体端到端验证：轻触开关键开始，按住麦克风说话，松开后再轻触开关键结束。自然快速操作下，开关键到麦克风约 `165 ms`，麦克风到结束开关键约 `239 ms`，复杂随机语句可完整转写。

Codex Voice 链路也已通过实体端到端验证。v0.1 可运行 `--tv-codex-voice-live`：轻触 TV 键打开或关闭 Voice，按住麦克风说话，松开即结束本段语音。实体验收中 TV 完成打开和关闭、GPT 正确理解并回复，程序退出后确认反引号恢复。当前无驱动实现有一项明确限制：该模式运行期间，电脑实体键盘的反引号键也会触发 Codex Voice；程序退出后按键立即恢复，不写入注册表，也不要求重启。设备级无损区分留给未来可选 HID filter 版本。

## 已确认的测试设备

- Windows 名称：`小米蓝牙语音遥控器`
- Bluetooth LE 地址：不写入仓库；工具按已配对设备名称自动发现
- Vendor ID：`0x2717`（Xiaomi）
- Product ID：`0x32B8`
- 标准服务：HID `0x1812`、Battery `0x180F`、Device Information `0x180A`
- 自定义服务：`000001BF-...`、`8A7A0001-...`、`AB5E0001-...`

Windows 已把按键接口识别为 `HID Keyboard Device`。目前没有发现普通 Windows 录音设备，因此麦克风暂按厂商自定义 BLE 数据通道处理。

## 运行只读探针

列出 Raw Input 设备：

```powershell
dotnet run --project src/MiVibe.Remote.Probe -- --list
```

监听目标遥控器 20 秒（只打印，不触发映射）：

```powershell
dotnet run --project src/MiVibe.Remote.Probe -- --listen 20
```

调试时监听所有键盘/HID 输入源：

```powershell
dotnet run --project src/MiVibe.Remote.Probe -- --listen 20 --all
```

只读枚举 BLE GATT 服务和 characteristic：

```powershell
dotnet run --project src/MiVibe.Remote.GattProbe -- --list
```

GATT 通知模式不会写入厂商 characteristic，只会设置并在结束时清除标准 BLE 通知订阅描述符。

启动 TV 键控制的 Codex Voice 语音桥（示例运行 45 秒）：

```powershell
dotnet run --project src/MiVibe.Remote.GattProbe -- --tv-codex-voice-live 45 --out logs/tv-codex-voice.wav
```

看到 `TV-controlled Codex Voice armed` 后，轻触 TV 键打开 Voice，再按住遥控器麦克风说话。运行窗口内请暂时不要使用电脑键盘的反引号键；中止或正常退出都会释放临时钩子。

常驻运行 Typeless 与 Codex Voice 共用的语音桥：

```powershell
dotnet run --project src/MiVibe.Remote.GattProbe -- --resident
```

常驻模式没有固定倒计时，按 `Ctrl+C` 安全退出。它不会把整段常驻会话无限保存在内存中；开关键仍负责 Typeless 的轻触开始/结束，TV 键负责 Codex Voice，实体麦克风键负责按住期间的实际采音。

## 托盘 MVP

构建后可直接启动无控制台窗口的托盘应用：

```powershell
dotnet build src/MiVibe.Remote.Tray/MiVibe.Remote.Tray.csproj
./src/MiVibe.Remote.Tray/bin/Debug/net9.0-windows10.0.26100.0/MiVibe.Remote.Tray.exe
```

应用启动后自动连接遥控器。右键任务栏通知区域中的 MiVibe 图标可查看状态，并执行启动、暂停、音频路由检查和安全退出。暂停和退出不会强制结束后台进程，而是请求语音桥完成 `MIC_CLOSE`、取消 BLE 通知订阅并恢复临时按键钩子。

每次启动的详细日志保存在托盘程序目录下的 `logs` 文件夹。当前仍是开发构建，尚未添加开机启动、安装器和正式图标。

只读检查当前音频路由：

```powershell
dotnet run --project src/MiVibe.Remote.GattProbe -- --audio-status
```

推荐状态是录音和通信录音都使用 `CABLE Output`，播放使用 AirPods 或其他耳机。AirPods 麦克风不应成为默认输入，否则 Windows 可能打开蓝牙免提语音配置，绕开遥控器输入并降低耳机播放质量。v0.1 只检测和提示，不静默修改全系统默认音频设备。

详细路线见 [开发计划](docs/DEVELOPMENT_PLAN.md)。

项目的重要选择记录在 [决策记录](docs/DECISIONS.md)，每轮工作和验证证据记录在 [开发过程日志](docs/PROCESS_LOG.md)。
实测按键与键码维护在 [硬件按键记录](docs/HARDWARE_MAP.md)。
BLE 服务与 characteristic 维护在 [GATT 服务记录](docs/GATT_MAP.md)。

## 安全原则

项目不会默认自动确认权限对话框。类似 “Allow once” 的动作必须显式启用、限制到指定应用，并保留可见反馈和紧急停用方式。
