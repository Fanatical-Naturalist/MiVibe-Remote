# MiVibe Remote

把小米蓝牙语音遥控器变成 Windows 上的 Vibe Coding 控制器。

![MiVibe Remote 遥控器按键指南](docs/assets/mivibe-remote-user-guide-dark.png)

当前公开预览版：`0.2.0-alpha.1`。它已经加入单实例托盘、蓝牙重新连接、电量提示、开机启动、深浅主题和按键工作流说明，适合源码体验与真实硬件测试；安装包和完整发布验收仍在后续阶段。详见 [0.2 发布范围](docs/RELEASE_V0.2_SCOPE.md) 与 [Changelog](CHANGELOG.md)。

当前冻结基线：`v0.1.0-prototype`。这是已通过本机实体测试、供日常使用和回归的本地基线；冻结能力和限制见 [v0.1.0-prototype 说明](docs/RELEASE_V0.1.0_PROTOTYPE.md)。

> Preview 提示：当前方案面向特定小米蓝牙语音遥控器和 Windows 11，依赖 Typeless/Codex、VB-CABLE 及用户自己的快捷键设置。请先阅读下方的已知限制，不要把它视为通用遥控器驱动。

当前仓库处于硬件验证与可用原型阶段。第一版复用 Windows 自带的 Bluetooth LE HID 驱动，目标是不安装自研内核驱动即可完成：

- 精确区分遥控器与普通键盘；
- 记录每个实体按键产生的原始事件；
- 将单击、双击、长按映射为可配置快捷键或受控动作；
- 通过 ATVV 解码遥控器麦克风，并经 VB-CABLE 提供给 Typeless 等语音应用。

Typeless 分离式语音链路已完成实体端到端验证：轻触开关键开始，按住麦克风说话，松开后再轻触开关键结束。自然快速操作下，开关键到麦克风约 `165 ms`，麦克风到结束开关键约 `239 ms`，复杂随机语句可完整转写。

Codex Voice 链路也已通过实体端到端验证。冻结的 v0.1 使用 TV 键并接受运行期间占用电脑反引号的限制。0.2 将实体入口迁移到遥控器菜单键；用户在 Codex 中用 `Ctrl + Alt + Numpad Multiply` 录入快捷键，Codex 当前会把它显示并保存为 `Ctrl + Alt + *`，MiVibe 发送同一规范化组合。Typeless 继续使用独立的 `Numpad Divide`；Home 每次新按下发送一次扩展键 `Delete`，长按不会连续删除；TV、电脑反引号以及裸 `/`、`.`、`*` 均保持原始输入。

0.2 当前仍是免驱用户态方案：常驻期间会拦截系统中的实体 `Menu/Application` 和 `Home` 键，因此电脑键盘若带这些键，Menu 会触发 Codex Voice，Home 会发送一次扩展键 `Delete`。这些冲突会在 0.2 发布说明中明确披露；暂停语音桥或安全退出后立即恢复原键行为。遥控器上带 `<` 图标的返回键以及音量键在当前 Windows/固件组合下没有用户态事件，0.2 暂不映射；要只区分遥控器按键或补齐缺失事件，需要后续设备级 HID 过滤能力。

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

启动菜单键控制的 Codex Voice 语音桥（示例运行 45 秒）：

```powershell
dotnet run --project src/MiVibe.Remote.GattProbe -- --menu-codex-voice-live 45 --out logs/menu-codex-voice.wav
```

先在 Codex 设置中把 Voice 快捷键录入为 `Ctrl + Alt + Numpad Multiply`；设置页最终显示 `Ctrl + Alt + *` 属于当前 Codex 的正常规范化结果。Typeless 继续使用小键盘除号 `Numpad Divide`。看到 `Menu-controlled Codex Voice armed` 后，轻触遥控器菜单键打开 Voice，再按住麦克风说话。Home 每次新按下只发送一次扩展键 `Delete`，长按不连删；TV 键、电脑反引号及裸 `/`、`.`、`*` 保持原始输入。常驻期间，电脑键盘上的实体 `Menu/Application` 与 `Home` 键也会被同一免驱钩子占用；暂停、中止或安全退出后恢复原键行为。

常驻运行 Typeless 与 Codex Voice 共用的语音桥：

```powershell
dotnet run --project src/MiVibe.Remote.GattProbe -- --resident
```

常驻模式没有固定倒计时，按 `Ctrl+C` 安全退出。它不会把整段常驻会话无限保存在内存中；开关键负责 Typeless 的轻触开始/结束，菜单键负责 Codex Voice，实体麦克风键负责按住期间的实际采音，Home 每次新按下负责一次扩展键 `Delete`，TV 键保持原始输入。

## 从源码启动 0.2 Preview

准备条件：

- Windows 11 x64 与 .NET 9 SDK；
- 已在 Windows 中配对的小米蓝牙语音遥控器；
- 已安装 VB-CABLE，并将 `CABLE Output` 设为默认录音和默认通信录音设备；
- Typeless 语音输入绑定 `Numpad Divide`；Codex Voice 绑定 `Ctrl + Alt + Numpad Multiply`（设置页可能显示为 `Ctrl + Alt + *`）。

构建并启动无控制台窗口的托盘应用：

```powershell
dotnet build src/MiVibe.Remote.Tray/MiVibe.Remote.Tray.csproj -c Release
./src/MiVibe.Remote.Tray/bin/Release/net9.0-windows10.0.26100.0/MiVibe.Remote.Tray.exe
```

应用启动后自动连接遥控器。右键任务栏通知区域中的 MiVibe 图标可查看状态并执行连接/重新连接、暂停、音频路由检查、开机启动和安全退出；双击图标打开可切换深色/浅色主题的极简状态窗口。窗口包含依据用户自有实拍图校准、去除品牌标识的遥控器工业设计插画、当前按键职责、两条语音工作流和克制的 BLE/ATVV/VB-CABLE 链路提示。关闭状态窗口只隐藏到托盘。暂停、主动重连和退出都不会强制结束后台进程，而是先请求语音桥完成 `MIC_CLOSE`、取消 BLE 通知订阅并恢复临时按键钩子。

0.2 Preview 会读取标准蓝牙电量服务，并在托盘和状态窗口中区分“实时电量”“上次读取”和“未知”。开机自动启动默认关闭，启用后只写入当前用户的 Windows 登录启动项。每次启动的详细日志保存在托盘程序目录下的 `logs` 文件夹；安装器和正式图标仍待后续发布阶段完成。

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

## 许可证

项目源码采用 [MIT License](LICENSE)。第三方软件（包括 VB-CABLE、Typeless、Codex 和 NAudio）分别遵循其自身许可证与使用条款，不随本仓库重新授权。
