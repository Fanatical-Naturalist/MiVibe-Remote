# MiVibe Remote

[English](README.md) | 简体中文

把小米蓝牙语音遥控器变成 Windows 11 上用于语音输入、Typeless Translate 和 Codex 任务切换的实体控制器。

> **0.3.0-alpha.2 硬件预览版。** 返回键 Delete、菜单键 Typeless Translate，以及音量键按 Codex 活动视图顺序切换相邻任务，均已通过用户实测。控制中心新增增强按键启用、三步校准和明暗主题界面。当前仅在小米蓝牙语音遥控器 2 Pro（`VID 2717 / PID 32B8 / REV 00A4`）上验证，其他遥控器及其余生命周期测试尚未完成。

**[下载 Windows x64 版 0.3](https://github.com/Fanatical-Naturalist/MiVibe-Remote/releases/tag/v0.3.0-alpha.2)** · [本次更新](docs/RELEASE_NOTES_0.3.0-alpha.2.md)

## 功能

- 通过 VB-CABLE 将遥控器麦克风音频送入 Windows。
- 用遥控器开关键开始或结束 Typeless 输入。
- 用菜单键打开 Typeless Translate。
- 启用增强按键后，返回键执行单次 Delete；音量键切换 Codex 相邻任务，活动视图按当前已加载列表的顺序切换。
- 在更新后的明暗主题控制中心查看连接、电量、增强按键状态与三步校准。
- 语音桥意外断开后自动尝试重连。
- 可选择登录后自动启动，并检查 VB-CABLE 默认输入与当前 Windows 输出；可以识别 AirPods 配置，但 AirPods 不是必需设备。MiVibe 不会自动切换系统音频设备。

## 使用要求

- Windows 11 x64 24H2 或更高版本。
- 电脑支持 Bluetooth Low Energy（低功耗蓝牙）。电脑不需要 Type-C 接口；遥控器的 Type-C 只用于充电。
- 已在 Windows 中配对、设备名为 `小米蓝牙语音遥控器` 或 `MI RC` 的遥控器。当前只保证上述 `VID 2717 / PID 32B8` 实机配置可用；设备名称相同不代表其他批次一定兼容。
- 另行安装 [VB-CABLE](https://vb-audio.com/Cable/)。
- 已安装并登录 Typeless 和/或 Codex。

## 快速开始

1. 从 [0.3 Releases](https://github.com/Fanatical-Naturalist/MiVibe-Remote/releases/tag/v0.3.0-alpha.2) 下载 **Setup EXE** 或完整便携包。升级前先“安全退出” MiVibe，安装程序会沿用原安装目录并请求管理员权限。旧版 0.2 包不含增强组件；已配置原有按键映射的电脑无需重复修改。
2. 在 Windows 蓝牙设置中配对遥控器，然后安装 VB-CABLE。
3. 将 **CABLE Output** 同时设为“默认录音设备”和“默认通信录音设备”。
4. 重启 Windows。如使用 Typeless，请在“设置 → 键盘快捷键 → 语音输入”中添加 `Numpad Divide`；映射生效后可直接轻触遥控器开关键录入，不要求电脑配有实体数字小键盘。
5. 将 Typeless Translate 配为 **右 Shift + T**。音量导航无需新增 Codex 快捷键：活动视图按当前已加载列表上下切换，普通视图沿用默认 **Ctrl + PageUp / PageDown**。
6. 在控制中心点击 **启用增强按键**，允许 Windows 管理员提示；等待提示后，依次短按并松开 **返回 → 音量＋ → 音量－**。校准阶段不会删除字符或切换任务。每次重新启动应用，都需手动启用并重新校准；登录自动启动不会自动批准增强组件的管理员权限。驱动宿主断线后会尝试自动恢复，恢复后只需跟随提示重新校准。

**暂停遥控器** 会停止语音、增强按键和应用的 Home/Menu 映射。恢复时先点击 **连接遥控器**，再手动启用增强按键并完成校准；暂停期间无法点击“启用增强按键”。

如果安装时取消了按键映射，可从开始菜单运行 **MiVibe Remote → Enable Remote Key Mapping**，然后重启 Windows。需要核对安装包时，请下载同一 Release 中的 `SHA256SUMS.txt`，并用 PowerShell 的 `Get-FileHash` 比较 SHA-256。

## 按键

| 遥控器按键 | 当前功能 |
| --- | --- |
| 轻触开关键 | 开始或结束一次 Typeless 输入 |
| 按住麦克风键 | 按住说话；松开结束当前语音段 |
| 方向环 / 中心键 | 导航 / Enter |
| Home | 删除光标后的一个字符 |
| 菜单键 | 打开 Typeless Translate |
| TV | 保留原始输入，MiVibe 不处理 |
| 返回 | 每次按下执行一次 Delete，长按只执行一次；需启用增强按键 |
| 音量＋ / − | 活动视图按已加载列表切换上一行 / 下一行任务；普通视图沿用默认任务导航。需启用增强按键且 Codex 在前台 |

## 重要限制

- 必需的扫描码映射作用于整个系统：启用后，所有实体键盘的 `F5` 都会变为 `F13`；任何发出扫描码 `E0 5E` 的按键（通常是 Power）都会变为 `Numpad Divide`。
- MiVibe 未暂停时还会拦截电脑实体键盘上的 `Menu/Application` 和 `Home`，包括语音断线或等待重连期间。点击控制中心的“暂停遥控器”或“安全退出”后恢复原行为。
- 在已测试的遥控器上，连续按住麦克风约 60 秒后可能停止。可在同一次 Typeless 会话内松开后再次按住，继续输入。
- 如果菜单键没有打开翻译，请先在电脑键盘上测试右 Shift + T。按住电脑上的修饰键时，增强删除和任务切换会跳过，避免快捷键组合冲突。
- 活动视图到已加载列表的首尾会停止，不绕回，也不自动加载更多任务。请先关闭任务菜单；菜单打开或当前任务无法明确识别时，MiVibe 跳过本次操作。普通视图仍使用 Ctrl + PageUp / PageDown；若被右侧面板用于切换标签，请先点击输入框。无需新增 Codex 快捷键。
- VB-CABLE、Typeless 和 Codex 均为第三方产品，不随 MiVibe 一起分发。
- 当前安装包尚未进行代码签名，Windows SmartScreen 可能提示“未知发布者”。运行前请核对 Release 中公布的 SHA-256 校验值。
- 卸载程序只会删除与 MiVibe 完全一致的按键映射，并可能要求重启 Windows；不会改动其他软件或用户已有的映射。

## 卸载

1. 在 MiVibe 托盘窗口中点击“安全退出”。
2. 打开“Windows 设置 → 应用 → 已安装的应用”，卸载 **MiVibe Remote**，并批准管理员权限。
3. 如果卸载程序提示重启，请重启 Windows，使实体键盘映射恢复生效。VB-CABLE、Typeless、Codex 和蓝牙配对不会被删除。

更完整的安装与卸载说明见英文版 [Quick Start](docs/QUICK_START.md)，当前变更和验收边界见 [0.3 发布说明](docs/RELEASE_NOTES_0.3.0-alpha.2.md)。

## 从源码构建

```powershell
dotnet build src/MiVibe.Remote.Tray/MiVibe.Remote.Tray.csproj --configuration Release
```

制作完整 0.3 包还需先运行 `tools/Build-KeyBridge.ps1`，将本地 Frida 17.15.3 与 Python 打包；构建工具使用 PyInstaller 6.14.2。终端用户无需另装 Python。随后用 .NET 9 SDK 和 Inno Setup 运行 `tools/Publish-Release.ps1`。贡献者说明见 [Development](docs/DEVELOPMENT_PLAN.md)。

## 许可证

MiVibe Remote 使用 [MIT License](LICENSE)。第三方组件继续适用各自的许可证，详见 [Third-Party Notices](THIRD-PARTY-NOTICES.md)。
