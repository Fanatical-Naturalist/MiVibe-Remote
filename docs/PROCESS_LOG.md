# 开发过程日志

## 记录约定

- 每个工作阶段记录：目标、执行内容、验证结果、遗留问题、下一步。
- 重要产品或架构选择同步写入 `DECISIONS.md`。
- 不在日志中记录账号令牌、蓝牙密钥、私有邮箱等敏感信息。

## 2026-08-19：项目启动与硬件探测

### 目标

确认小米蓝牙语音遥控器在 Windows 中暴露的设备接口，并判断最小可行实现路线。

### 执行内容

1. 阅读用户提供的设备截图和目标界面参考，只把图片作为需求资料。
2. 枚举 Windows 蓝牙、PnP、HID 与音频设备。
3. 查询遥控器相关的 HID 子设备与 GATT 服务。
4. 编写 `.NET 9` Raw Input 只读探针。
5. 建立产品与技术开发计划。

### 已验证结果

- Windows 设备名：小米蓝牙语音遥控器。
- Bluetooth LE 地址属于设备唯一标识，不写入开源仓库；工具改为按已配对设备名称发现。
- VID/PID：`0x2717 / 0x32B8`。
- Windows 创建了标准 `HID Keyboard Device`，Usage Page/Usage 为 `0x01 / 0x06`。
- 发现标准 HID、电池、设备信息服务以及 3 个厂商自定义 GATT 服务。
- 没有发现对应的普通 Windows 音频输入端点。
- 探针编译通过：0 个错误、0 个警告。
- 探针能够从 35 个 Raw Input 设备中唯一识别目标遥控器。

### 未完成验证

- Codex 后台执行会话没有收到物理桌面输入，因此尚未采集每个实体按键的真实按下/松开键码。
- 需要用户在交互式 PowerShell 中运行探针并依次按键。

### 技术结论

- v0.1 使用 Windows 自带 BLE HID 驱动和用户态 Raw Input，不开发内核驱动。
- 麦克风需要单独枚举并研究厂商自定义 GATT characteristic。
- Codex Voice、Typeless 等快捷键都做成用户可修改配置，不硬编码为永久规则。

## 2026-08-19：工作目录纠正

### 用户要求

- 不在 C 盘维护项目。
- 项目和所有相关文档统一放到 `E:\05-Projects\2026-08-19-XiaomiVibeCoder\`。
- 这是用户第一个 GitHub 开源项目，需要逐步解释并持续记录过程。
- GitHub 连接在确实需要发布时再打开网页请求授权。

### 迁移方案

1. 确认 C 盘源项目存在。
2. 确认 E 盘目标目录存在且为空。
3. 在迁移前补充决策记录和过程日志。
4. 整体移动包含 `.git` 的项目目录。
5. 校验 E 盘仓库状态，并确认 C 盘旧项目不再存在。

### 下一步

迁移校验后，让用户只完成一件事：在交互式终端采集遥控器实体按键事件。

### 迁移结果

- 第一次直接跨盘移动时，Windows 报告隐藏/权限错误；检查后确认文件内容实际上已经迁到 E 盘，只在 C 盘留下空的 `.git` 目录壳。
- E 盘关键源码、README、开发计划、决策记录、过程日志和 Git 元数据均存在。
- 在 E 盘重新编译成功：0 个错误、0 个警告。
- 从 E 盘运行探针，再次唯一识别到 `VID 0x2717 / PID 0x32B8`、Usage `0x01 / 0x06` 的目标遥控器。
- C 盘残留目录经确认不包含任何文件后已删除。
- 当前项目唯一工作目录为 `E:\05-Projects\2026-08-19-XiaomiVibeCoder\`。

## 2026-08-19：第一轮实体按键采样

### 用户反馈

- 返回键按下时没有任何反应。

### 日志分析

- 方向上、下、左、右和确认键均产生完整的按下/松开 Raw Input 事件。
- 确认键之后到 Home 键之前存在约 12 秒空档，与“返回键无反应”一致。
- 后续日志依次出现 Home、Apps/Menu、反引号和 F5；根据既定采样顺序，分别高置信对应 Home、菜单、TV 和麦克风。
- F5 短按与长按特征明显；长按期间约每 32ms产生一次重复 key-down，松开后产生 key-up。
- 音量加减没有观察到可明确区分的目标 Raw Input 事件，需要单独复测。

### 本轮改进

- 新增 `docs/HARDWARE_MAP.md`，区分已确认映射、高置信推断和待定项。
- `--all` 诊断模式增加 Windows 全局翻译键盘事件。
- 增加 `WM_APPCOMMAND` 日志，用于发现浏览器返回、媒体键等高层应用命令。

### 下一步实验

只测试返回、音量加和音量减，避免采样顺序造成歧义。如果增强诊断仍无事件，则开始枚举并订阅厂商自定义 GATT characteristic。

## 2026-08-19：返回与音量键针对性复测

### 验证结果

- 两个普通键盘空格标记以及后续普通键盘组合键均被 Raw Input 和全局翻译钩子捕获。
- 用户在标记之间多次按遥控器返回、音量加和音量减，三类按键均无 Raw Input、翻译键盘或 `WM_APPCOMMAND` 事件。
- 可以排除探针未运行、目标过滤错误以及 Windows 仅做高层按键翻译这三种情况。

### 路线调整

- 新增独立 `MiVibe.Remote.GattProbe`，避免把蓝牙逆向逻辑混入按键映射原型。
- 第一阶段只枚举服务、characteristic 和 descriptor，不修改厂商数据。
- 通知模式仅配置标准 CCCD 通知/指示订阅，并在结束时清除。
- 工具按配对名称自动发现遥控器，避免把用户设备的唯一蓝牙地址提交到开源仓库。

### 工具链记录

- 首次把 `Microsoft.Windows.SDK.NET.Ref 10.0.26100.87` 作为普通 `PackageReference` 时，NuGet 返回 `NU1213 DotnetPlatform` 不兼容。
- 原因：新版包是 Windows SDK targeting pack，不是普通应用依赖。
- 按微软官方方式改为项目属性 `WindowsSdkPackageVersion=10.0.26100.87`，由 Windows 目标框架隐式解析引用。

### 只读 GATT 枚举结果

- GATT 探针编译成功：0 个错误、0 个警告。
- 按配对名称连接成功，共发现 9 个服务。
- 标准 HID 服务 `0x1812` 的 characteristic 枚举被 Windows 拒绝，状态为 `AccessDenied`，符合系统 HID 驱动占用的预期。
- `8A7A...` 服务暴露 3 个 Notify characteristic。
- `AB5E...` 服务暴露 2 个 Notify characteristic；公开遥控器协议资料将该 UUID 标识为 Android TV Voice（ATVV）服务。
- `0xFE59` 含固件升级相关 characteristic，为降低风险从按键监听实验中排除。
- 下一轮监听只允许 `8A7A...` 和 `AB5E...` 两个服务，不写入厂商 characteristic。

## 2026-08-19：厂商 GATT 通知采样

### 验证结果

- `8A7A...` 的 3 个 characteristic 与 `AB5E...` 的 2 个 characteristic 均成功订阅 Notify。
- 用户依次操作 Home、返回、音量加、音量减、麦克风短按/长按后，没有收到任何 GATT 通知。
- 5 秒无人操作复测能正常等待、取消订阅并以退出码 0 结束，排除程序提前退出。

### 结论

- 返回和音量键不通过已公开的厂商 GATT Notify characteristic 上报。
- ATVV 语音通道需要主机握手和 `MIC_OPEN` 类控制命令，仅订阅不足以启动音频流。
- 按键研究转向 Windows 已解析的 HID preparsed data/report capabilities，检查缺失按键是否存在于 descriptor 但未被键盘翻译层映射。

### HID descriptor 查询记录

- `GetRawInputDeviceInfo(RIDI_PREPARSEDDATA)` 对目标键盘类型设备返回零长度。
- 这类 Raw Input 键盘句柄不直接公开 preparsed data；探针增加零访问权限 `CreateFile` + `HidD_GetPreparsedData` 回退路径。
- 回退只查询系统已解析的 descriptor 能力，不请求读写权限，也不读取或发送 HID report。

### HID descriptor 结果

- 零访问权限 `HidD_GetPreparsedData` 成功。
- Collection 为 Generic Desktop / Keyboard（`0x0001 / 0x0006`）。
- 最大 input report 长度为 121 字节，明显大于普通键盘报告。
- Report ID `0x01` 声明 Keyboard Page usage range `0x0000–0x00FE`。
- Report ID `0x06`、`0x07`、`0x08` 声明 Vendor Page `0xFF00` usage range `0x0000–0x00FF`。
- Back 常见的 Consumer Page `0x0C / 0x0224` 不在该 collection capability 中；音量 `0x80/0x81` 位于 Keyboard Page 声明范围内，可能只是未被 Windows 翻译。
- 下一步尝试共享、只读打开 HID input report 流；失败时再评估系统保留键盘 collection 的限制与 filter driver 方案。

### 原始 HID report 访问结果

- 使用共享模式、只请求 `GENERIC_READ` 打开目标 keyboard collection，Windows 返回 `Access Denied (5)`。
- 这是系统键盘驱动保留 collection 的边界；继续增加用户态键盘钩子无法获得 report ID `0x06/0x07/0x08` 的原始数据。
- 本机已安装 Visual Studio Community 2022，但没有安装 Windows Driver Kit（WDK）或 KMDF headers/targets。
- 若选择内核路线，需要先安装与 Visual Studio 2022 匹配的 WDK 26100 系列、建立测试签名环境，并接受管理员权限、驱动安装和可能重启的成本。
- 公开发行还涉及 Microsoft 驱动签名/Partner Center 流程，不能把本地测试签名包直接当作面向普通用户的 release。

### 待用户架构决策

1. 先发布纯用户态 v0.1，使用已捕获的按键，缺失键标为不支持；filter driver 后续作为可选高级组件。
2. 暂停用户态 MVP，立即进入测试签名的 HID filter driver 研发。

### 用户决定

- 用户选择 A：先完成纯用户态 v0.1。
- 当前阶段不安装 WDK、不启用测试签名、不修改 Windows 启动设置。
- 返回/音量兼容性问题转入 Future，不再阻塞映射引擎、常驻应用、UI 和首次 GitHub 发布。
- 下一项需要确认的设计维度：先交付最小可用常驻版，还是直接建设完整可视化映射界面。

### 工具体验问题

- Windows PowerShell 5 将 UTF-8 原生程序输出通过管道交给 `Tee-Object` 时出现中文设备名乱码；数据 UUID 和十六进制内容不受影响。
- 后续公开工具应支持直接写 UTF-8 日志文件，避免依赖 PowerShell 旧版管道编码。

## 2026-08-20：确认 v0.1 交付形态

### 用户决定

- 用户选择“先做最小可用常驻版”。
- 先实现后台常驻、设备过滤、映射状态机、快捷键注入、安全暂停与诊断日志。
- 完整的遥控器可视化映射界面排在核心稳定性验证之后。

### 决策影响

- 下一阶段可以直接开发每天可用的 M1，不让界面设计阻塞核心能力验证。
- 核心配置和动作模型必须与未来 UI 解耦，避免制作图形界面时重写映射引擎。
- 下一项待确认：首个预设中各个已识别实体按键的默认职责，尤其是哪些按键负责 Codex Voice、Typeless 与暂停映射。

### 默认预设确认

- 用户选择单一 Vibe Coding 预设，不引入 Codex/Typeless 双模式。
- 麦克风实体键映射为更常用的 Typeless，快捷键由用户截图确认为右 `Ctrl + 右 Shift`。
- 菜单实体键映射为 Codex Voice，当前快捷键为 `Ctrl + \``。
- Codex 已固定在 Windows 开始按钮旁的任务栏，不需要占用遥控器实体键进行启动或聚焦。
- 释放出的可用实体键优先考虑高频删除动作；下一步确认 `Backspace` 和 `Delete` 的物理按键及手势安排。

### 删除键确认与麦克风需求升级

- 用户选择 Home 单击只映射 `Backspace`，v0.1 暂不提供 `Delete` 长按动作。
- 用户补充：电脑现有系统麦克风已经损坏，麦克风实体键必须同时启动 Typeless 与小米遥控器自身的人声采集。
- 这使遥控器麦克风从后续实验能力升级为核心使用需求。

### 协议与 Windows 音频边界复核

- Android TV 参考遥控器源码确认了 ATVV 能力查询、`MIC_OPEN`、`MIC_CLOSE`、音频通知以及 8/16 kHz ADPCM 路径。
- 此前零通知的原因可以由“主机未发送能力协商和开麦命令”解释，下一步应实施受限的 ATVV 握手探针。
- Windows 普通应用从录音端点读取音频；要让 Typeless 接收解码后的遥控器 PCM，需要一个系统可枚举的录音端点。
- 微软提供的 SYSVAD 是 WDM 虚拟音频驱动样例，说明自建虚拟麦克风会重新引入 WDK、管理员安装与驱动签名成本。
- 下一项待确认：原型阶段是否允许依赖成熟的已签名虚拟音频桥，以便先验证“遥控器 → Typeless”的完整链路。

### 虚拟音频桥路线确认

- 用户选择原型阶段使用成熟、已签名的虚拟音频桥，不立即自研 Windows 虚拟麦克风驱动。
- 本机已注册的录音端点中没有发现 VB-CABLE、VoiceMeeter 或同类虚拟音频线；NVIDIA Virtual Audio 不提供本场景需要的播放到录音桥。
- VB-CABLE 官方版本支持 Windows 11，提供播放端与录音端之间的直接转发，功能范围最贴合原型。
- VB-CABLE 安装需要管理员权限和重启，许可为 donationware；开源仓库初期应只提供官方来源链接和配置说明，不把第三方二进制当作本项目代码提交。
- VoiceMeeter 也提供虚拟音频 I/O，但包含完整混音器，安装体积与使用复杂度均高于当前需求。
- 下一项待确认：选择轻量 VB-CABLE、较重的 VoiceMeeter，或等 WAV 采音验证成功后再锁定产品。

### 虚拟音频桥产品确认

- 用户选择 VB-CABLE 作为原型期的虚拟音频桥。
- MiVibe 将解码后的遥控器 PCM 写入 VB-CABLE 播放端，Typeless 从对应录音端采集。
- VB-CABLE 保持为独立第三方依赖：只从官方渠道获取，不把其二进制提交到开源仓库，也不未经用户批准静默安装。
- 在 ATVV 音频成功录制为 WAV 之前不急于安装 VB-CABLE，避免提前引入管理员操作和重启。
- 下一项待确认：若遥控器开麦或音频流启动失败，是否仍然触发 Typeless 快捷键。

### Typeless 失败策略与 ATVV 开发启动

- 用户选择即使遥控器开麦失败也触发 Typeless，由 Typeless 保留其他麦克风兜底能力。
- 新增 `--voice-capture` 实验命令，按 ATVV 1.0 执行能力查询、开麦、续期、关麦和通知清理。
- 实现 8/16 kHz IMA ADPCM 初始解码和单声道 16-bit WAV 输出，同时保留原始 ADPCM 便于复核。
- 安全限制：只有显式调用采音命令才写入 ATVV；能力响应 major version 不是 1 时停止；不访问 DFU 或其他厂商写通道。
- 第一次编译发现 `BluetoothCacheMode` 命名空间遗漏，补充 `Windows.Devices.Bluetooth` 引用后通过。
- 当前构建结果：0 个警告、0 个错误。
- 下一步需要用户知情运行一次 8 秒采音，在遥控器旁说话，并把终端输出及生成的 WAV 交给项目验证。

### 第一次 ATVV 1.0 采音结果

- CONTROL 与 AUDIO 两个 Notify 订阅均返回 `Success`。
- 能力查询收到 `0B0100000000780000`：版本 1.0、交互模式 On-request、音频帧大小 120 字节；codec capability 字节为 0，属于与规范不完全一致的固件行为。
- `MIC_OPEN` 收到 `04000200`：遥控器明确宣布以 16 kHz ADPCM、stream ID 0 开始音频。
- 8 秒后 `MIC_CLOSE` 收到 `0000`，说明开关麦控制链路完整成功。
- 失败边界：`AB5E0003` 没有交付任何音频通知，因此没有生成 WAV。
- 当前不能判定麦克风硬件失败；优先检查 Windows 实际 GATT PDU/MTU、CCCD 读回状态和连接吞吐参数。

### 第二版链路诊断

- 采音期间启用 `GattSession.MaintainConnection`，结束时恢复。
- 仅在采音期间请求 Windows 11 `ThroughputOptimized` BLE 连接参数，结束时释放请求。
- 输出 `GattSession.MaxPduSize` 和动态变化，用来和遥控器声明的 120 字节音频帧比较。
- 写入 Notify 后读回 CONTROL/AUDIO CCCD，避免仅凭写调用成功推断订阅已生效。
- 打印前 5 个音频通知的包长和短预览，原始音频仍不输出到终端。
- 增强版本构建通过：0 个警告、0 个错误。

### 第二次 ATVV 采音结果

- 用户使用方向上键作为开麦前的近期实体操作；该按键有效，且后续 `MIC_OPEN` 成功，排除遥控器未激活错误。
- Windows GATT session 为 `Active`，`maxPduSize=247`，大于遥控器声明的 120 字节音频帧。
- `ThroughputOptimized` 请求成功。
- CONTROL 与 AUDIO 的 CCCD 写入和读回均为 `Success / Notify`。
- 能力响应本次稳定报告 codec `0x02`，即 16 kHz ADPCM；`AUDIO_START` 同样报告 codec `0x02`。
- 仍未收到任何 AUDIO notification；MTU 不足、CCCD 未生效、连接未保持和遥控器未激活四项假设均被排除。
- 下一项最小实验：在程序显示 `Capturing` 后持续按住实体麦克风键并说话，检查固件是否存在物理隐私门控，同时观察 `START_SEARCH`/第二个 `AUDIO_START` 控制通知。

### 第三次 ATVV 采音成功

- 用户最初在 `%USERPROFILE%` 运行相对路径，因此出现 `CommandNotFoundException`；切换到 E 盘项目目录后操作正确。
- 程序主动 `MIC_OPEN` 后，遥控器先报告 `04000200`，但仍不发送音频。
- 用户在 `Capturing` 阶段持续按住实体麦克风键后，收到 `04030201`：reason `0x03`（Hold-to-Talk）、codec `0x02`（16 kHz ADPCM）、stream ID `0x01`。
- 随后收到 413 个音频包，每包 120 字节，共 49,560 字节 ADPCM。
- 成功解码为 99,120 个 16-bit PCM sample，生成的 WAV 为 16 kHz 单声道，时长约 6.195 秒。
- 结论：遥控器麦克风、BLE 音频通道和第一版 ADPCM 解码链路均已打通；该固件存在实体麦克风键隐私门控。
- 修正：后续 `AUDIO_START` 到达时更新当前 stream ID，避免物理 HTT 流启动后仍使用旧的 on-request stream ID 关麦。

### 音质验收与增益策略

- 用户确认 WAV 可以听到人声，声音清楚、语速正常、无杂音，仅音量略小。
- WAV 结构复核：16 kHz、单声道、16-bit PCM、约 6.195 秒。
- 音量统计：RMS 约 `-38.07 dBFS`；满幅样本 8 个，占约 `0.00807%`，其余绝大多数样本幅度较低。
- 采用默认 `+6 dB` 软件增益并做限幅保护，不修改原始 ADPCM；后续 UI 可调。
- `--gain-db` 支持 `-12` 到 `+24 dB`，便于不同遥控器和说话距离校准。
- 麦克风链路第一阶段验收通过，下一阶段进入实时 PCM 输出与 VB-CABLE/Typeless 集成。

### VB-CABLE 安装准备

- 用户明确要求开始安装 VB-CABLE。
- 安装包从 VB-Audio 官方下载域名获取：`VBCABLE_Driver_Pack45.zip`，官方标注为 2024-10、支持 Windows 11 32/64-bit/Arm64。
- 安装包保存到项目 `.local/third-party/vb-cable`，`.local/` 已加入 `.gitignore`，不会提交 GitHub。
- 下载文件大小：1,318,877 字节；SHA-256：`B950E39F01AF1D04EA623C8F6D8EB9B6EA5C477C637295FABF20631C85116BFB`。
- `VBCABLE_Setup_x64.exe` Authenticode 状态为 `Valid`；签名者为 `BUREL VINCENT Entrepreneur individuel`，证书 thumbprint 为 `A77952D93229D0EC36E2543081EEA7D125732B9C`。
- 已以可见管理员安装界面启动 64 位安装器；不使用静默安装，不自动重启，等待用户点击 `Install Driver` 并反馈结果。

### VB-CABLE 安装完成

- 用户点击安装后，安装器自动关闭并打开 VB-Audio 的感谢页面；这是安装完成后的正常行为，不是异常退出。
- Windows 已注册并启用以下音频端点：
  - 播放端 `CABLE Input (VB-Audio Virtual Cable)`：MiVibe 向这里写入实时 PCM。
  - 录音端 `CABLE Output (VB-Audio Virtual Cable)`：后续由 Typeless 选择为麦克风。
  - 播放端 `CABLE In 16ch (VB-Audio Virtual Cable)`：当前单声道语音原型不使用。
- 三个端点的设备状态均为 `1`（Active/已启用），因此当前不要求重启；若后续应用无法枚举端点或音频图初始化失败，再把重启作为排障步骤。
- 在 MiVibe 实时输出接通前，暂不把 Typeless 切换到 `CABLE Output`，避免 Typeless 暂时收到静音。
- 下一步：实现并验证 `遥控器 ATVV → ADPCM 解码 → +6 dB PCM → CABLE Input → CABLE Output` 的实时链路。

### VB-CABLE 实时输出原型

- 选择 NAudio 稳定版 `2.3.0` 访问 Windows WASAPI；官方 NuGet 包与源码仓库均标注 MIT 许可证。未采用 `3.0.0-preview` 预览版。
- 新增 `--voice-live [seconds]`：执行与已验证 WAV 采音相同的 ATVV 握手，同时把每个到达的 ADPCM 包连续解码为 16 kHz、单声道、16-bit PCM。
- 实时 PCM 复用已确认的默认 `+6 dB` 增益和 16-bit 限幅，然后以 Windows 共享模式写入 `CABLE Input`。
- 设备匹配会排除 `CABLE In 16ch`，避免误选多声道端点；可用 `--render-device` 覆盖默认名称。
- 实时模式仍保存 `.adpcm` 与 `.wav`，方便把“遥控器采音问题”和“虚拟音频路由问题”分开诊断。
- IMA ADPCM 解码器增加跨包保留 predictor/index 的流式状态，避免每个 120 字节包都从零状态开始而产生边界失真。
- 依赖恢复和 Debug 构建成功：0 个警告、0 个错误；帮助输出已确认新命令可见。
- Git 状态确认 `.local/` 安装文件未进入待提交列表。
- 待实体测试：运行 15 秒实时模式，在出现 `Live route armed` 后持续按住遥控器麦克风键说话，确认初始化成功且仍能生成 WAV；随后再让 Typeless 选择 `CABLE Output` 做端到端验收。

### VB-CABLE 实时输出实体测试成功

- `CABLE Input (VB-Audio Virtual Cable)` 以 Windows 共享模式成功初始化，输入格式为 16 kHz、单声道、16-bit PCM。
- 实体麦克风键触发第二个 `AUDIO_START`：`04030202`，reason 为 Hold-to-Talk、codec 为 16 kHz ADPCM、stream ID 为 `0x02`。
- 实时阶段收到 612 个 120 字节音频包，共 73,440 字节 ADPCM；解码为 146,880 个 PCM sample，约 9.18 秒。
- 测试生成 `logs/vb-cable-live-test.wav`（293,804 字节）和对应 `.adpcm`（73,440 字节）。
- 用户松开实体麦克风键后收到 `0002`，表示 stream ID `0x02` 正常停止；不是错误。随后程序安全执行 `MIC_CLOSE` 和取消订阅。
- 结论：`遥控器 → BLE ATVV → 流式 ADPCM 解码 → +6 dB PCM → CABLE Input` 已打通。尚待验证链路最后一段：`CABLE Output → Typeless → 文本框`。
- Typeless 官方指引确认可在 `Settings → Audio → Microphone` 中选择输入设备，并通过蓝色音量指示条验证声音。

### Typeless 输入音量表验证成功

- 用户在 Typeless 中选择 `CABLE Output` 后运行 30 秒实时桥测试，输入设备旁的蓝色音量条随遥控器人声跳动。
- 本轮识别到两次实体麦克风按住会话，分别使用 stream ID `0x03` 和 `0x04`；松开后均收到停止通知。
- 累计收到 980 个音频包，共 117,600 字节 ADPCM，解码为 235,200 个 PCM sample，约 14.7 秒。
- 生成 `logs/typeless-meter-test.wav` 和对应 `.adpcm`，程序正常执行 `MIC_CLOSE`、取消订阅并返回命令提示符。
- 结论：`遥控器 → BLE → 解码/增益 → CABLE Input → CABLE Output → Typeless 音频输入` 已完整连通。
- 尚待最后的功能验收：先用键盘快捷键手动启动 Typeless，验证语音能实际转成文字；随后再把快捷键触发与音频桥合并进常驻应用。

### Typeless 实际转写成功与 F5 冲突定位

- 用户在记事本中手动启动 Typeless，持续按住遥控器麦克风键说出测试句。
- Typeless 成功输出“这是小米遥控器通过 Vibe Cable 输入给 Typeless 的第一次测试”；其中把产品名 `VB-CABLE` 按读音写为 `Vibe Cable`，不影响链路验收。
- 端到端链路 `遥控器 → BLE → 解码/增益 → VB-CABLE → Typeless → 文本框` 首次完整通过。
- 记事本同时插入了大量重复的当前时间。原因不是错误转写：此前按键采样已确认遥控器麦克风键会发送 `F5`，而 Windows 记事本将 `F5` 映射为“插入当前日期和时间”；持续按住造成键盘自动重复。
- 该原始 F5 在浏览器、IDE 等应用中还可能触发刷新或运行，正式常驻应用必须屏蔽它，不能只额外注入 Typeless 快捷键。
- 新增短时 `--intercept-f5` 诊断模式：运行期间低级键盘钩子拦截所有非注入 F5，Raw Input 继续记录设备来源；不注入快捷键、不执行动作、不修改系统配置，定时退出后自动解除钩子。
- 实验目标：验证 F5 被拦截后 Raw Input 是否仍能区分小米遥控器与普通键盘。若成立，下一版可在用户态吞掉遥控器 F5，并为普通键盘重放原始 F5；若不成立，再明确评估全局保留键或 HID filter 方案。
- F5 拦截实验构建成功：0 个警告、0 个错误。

### F5 拦截实验结论与自动 Typeless 原型

- 20 秒实验期间，遥控器长按产生的 F5 自动重复和电脑键盘单次 F5 均被完全拦截，记事本没有插入时间；定时退出后钩子自动解除。
- 低级键盘钩子返回“已处理”后，同一批 F5 不再产生 `WM_INPUT`，日志只有 `target=unknown` 的翻译层事件，没有 `target=True/False` 的设备来源事件。
- 因此“先在低级钩子吞掉 F5，再用 Raw Input 区分来源并重放普通键盘”的直接方案不可行。
- 改用 ATVV 语音状态进行可靠关联：遥控器物理开麦会产生独有的 reason `0x03` Hold-to-Talk 通知，松开后产生停止通知，不依赖无法识别设备来源的 F5 翻译事件。
- 新增 `--typeless-live` 原型：
  - 命令运行期间临时屏蔽非注入 F5，避免时间插入、浏览器刷新或 IDE 运行等副作用。
  - 收到 ATVV HTT 开始时精确注入右 `Ctrl + 右 Shift`，自动启动 Typeless。
  - 收到 ATVV 停止时再次注入同一组合键，自动停止 Typeless。
  - 退出清理时若 Typeless 仍处于活动状态，会先停止 Typeless，再释放 F5 钩子。
- 2 秒无人按键冒烟测试通过：BLE、VB-CABLE 和 F5 钩子正常启动；没有误触发 Typeless；退出显示 `blockedEvents=0` 且电脑 F5 已恢复。
- 自动 Typeless 原型构建成功：0 个警告、0 个错误。
- 当前原型限制：命令运行期间电脑实体键盘 F5 也被临时占用；正式常驻版需增加与 ATVV 状态关联的延迟放行，或在高级版本评估设备级 filter driver。

### 自动 Typeless 第一次测试失败定位

- `--typeless-live` 正常收到两次 HTT 会话，累计 798 个音频包；VB-CABLE 实时输出和 WAV/ADPCM 保存成功。
- 日志在正确时刻打印了两组 `Typeless dictation started/stopped`，并成功屏蔽 339 个原始 F5 事件；记事本没有再次插入时间。
- 但 Typeless 实际没有启动，记事本无转写内容。因此失败点已缩小到快捷键模拟，不是 BLE、麦克风、解码、VB-CABLE 或 HTT 状态判断。
- 第一版 `SendInput` 使用右 `Ctrl`/右 `Shift` 的虚拟键码；Windows 接受了全部输入事件，但 Typeless 没有识别为用户设置的右侧修饰键组合。
- 修正为键盘扫描码注入：右 `Ctrl` 使用扩展扫描码 `E0 1D`，右 `Shift` 使用扫描码 `36`，按下和松开都显式发送。
- 新增 `--shortcut-test` 独立诊断：不连接蓝牙，5 秒后发送一次组合键尝试启动 Typeless，再过 5 秒发送一次尝试停止，快速隔离 Typeless 是否接受模拟扫描码。
- 扫描码版本构建成功：0 个警告、0 个错误。

### Typeless 拒绝模拟输入与物理 F5 路线

- 用户运行 `--shortcut-test` 后，虚拟键码版和物理扫描码版的右 `Ctrl + 右 Shift` 都没有启动 Typeless。
- `SendInput` 返回完整成功数量，说明 Windows 接受了事件；两种表达均被 Typeless 忽略，合理结论是 Typeless 的全局键盘监控主动排除了带注入标记的模拟输入。
- 继续修改 `SendInput` 已无价值；不能把控制台打印的“快捷键已发送”误当成 Typeless 已启动。
- 只读检查本机 Typeless 2.3.1 安装资源：应用为 Electron，多进程键盘监控由自己的 `global-keyboard`/`keyboard-helper` 与原生库实现。
- 应用内部状态机包含 `PRIMARY_KEY.DOWN/UP`、tap-or-hold、long-pressing、hands-free，以及 release-to-finish/push-to-talk 路径。这表明 Typeless 原生支持按下开始、长按说话、松开结束的交互。
- 没有发现面向外部程序注册的 `typeless://` URI；主进程 IPC 属于混淆后的 Electron 私有实现，不把它当作稳定开源集成接口。
- 下一项可逆实验：在 Typeless UI 中把“语音输入”快捷键临时改为遥控器实际发送的物理 `F5`。若 Typeless 自身吞掉该匹配键并按松开停止，MiVibe 只需运行音频桥，不再模拟 Typeless 快捷键。
- 该路线的显式限制：Typeless 运行时电脑实体键盘 F5 也会成为语音快捷键；后续若必须保留普通 F5，需再设计高级设备级方案。
- Typeless 安装资源的分析副本仅位于 `.local/analysis/typeless`，已被 Git 忽略，不会提交第三方私有代码或安装内容。

### Typeless 拒绝单独 F5 快捷键

- 用户在 Typeless 快捷键编辑界面按遥控器麦克风键时，界面能先识别到物理 `F5`，证明遥控器事件可被 Typeless 自身的原生键盘监控看到。
- 随后 Typeless 以“此快捷键已保留供系统使用”拒绝该配置，未能把语音输入快捷键保存为单独 F5。
- 结论：物理输入识别没有问题；阻塞点是 Typeless 的快捷键策略，同时它又拒绝 Windows 模拟输入，因此纯 `SendInput` 和直接 F5 配置两条用户态捷径均关闭。
- 不采用篡改 Typeless 私有 SQLite/Electron IPC 的方案：内部实现混淆、未暴露外部协议、随版本更新易失效，不适合作为可维护的开源项目基础。
- 当前架构分岔：
  1. 开发可产生非注入键盘事件的虚拟 HID 组件，使 Typeless 将右 `Ctrl + 右 Shift` 视为真实硬件输入；需要 WDK、管理员安装、测试签名/重启及后续公开签名规划。
  2. 维持纯用户态 v0.1，先由用户手动启动 Typeless，再用遥控器按住采音；一键体验延后到驱动阶段。
- 按 `grill-me` 流程暂停架构扩张，等待用户在“一键体验优先”与“先发布纯用户态 MVP”之间做基础选择。

### 确认虚拟 HID 与正式驱动签名路线

- 用户选择架构分岔的选项 1：继续实现一键体验所需的虚拟 HID，不把“手动启动 Typeless”作为最终产品交互。
- 用户同时明确提出：公开发布必须规划正式驱动签名，不能停留在开发机测试签名状态。
- 本机基线复核：Windows 11 25H2（build 26200.9168）、x64；HVCI/内存完整性已开启。
- 开发环境复核：Visual Studio 2022 Community 已安装；WDK、KMDF targets 与 WDK Visual Studio 扩展尚未安装。
- 微软官方技术边界复核：Virtual HID Framework（VHF）的 HID source driver 必须运行在内核模式，因此该路线会新增一个最小 KMDF 驱动，而不是继续包装 `SendInput`。
- 正式发布链路纳入项目里程碑：Partner Center Hardware Developer 账号、EV 代码签名证书、对应目标系统版本的 HLK、HVCI 兼容测试、微软签名提交，以及安装/卸载/回滚和哈希说明。
- 安全边界：当前没有关闭 Secure Boot、HVCI 或修改 `TESTSIGNING`；在测试环境方案得到用户确认前，不改变日用电脑的启动安全配置。
- 下一项需求访谈只确认首发支持矩阵，因为它会直接决定驱动项目目标、HLK 版本、测试设备数量和正式签名成本。

### 锁定虚拟 HID 首发支持矩阵

- 用户选择首发范围选项 1：Windows 11 24H2/25H2 x64。
- v0.1 暂不承诺 Windows 10、Windows 11 Arm64 或更早的 Windows 11 版本；这些平台只有在后续补齐构建、实体测试和签名验证后才能加入支持列表。
- 当前开发机 Windows 11 25H2 x64 可承担日常集成验证；仍需为 Windows 11 24H2 准备独立测试环境。
- GitHub Release、README、安装器和故障模板必须明确显示操作系统版本与 x64 架构要求。
- 下一项需求访谈确认测试签名环境；在该决定完成前，不修改当前电脑的 Secure Boot、HVCI 或启动签名设置。

### 确认隔离的驱动测试签名环境

- 用户选择测试环境选项 1：独立 Windows 11 x64 虚拟机。
- 测试证书、测试签名模式、驱动反复安装/卸载和崩溃恢复验证全部限制在虚拟机中；通过快照保证可恢复。
- 当前日用电脑继续保持 Secure Boot、HVCI/内存完整性和启动签名设置不变。
- 早期虚拟机使用模拟控制程序验证虚拟 HID 报告和 Typeless 识别；实体遥控器与 BLE/ATVV 的最终整合测试等待微软预生产或正式签名包后在主机执行。
- 下一步先只读检查本机已有虚拟化软件、Windows 功能和固件虚拟化状态，再决定虚拟机平台，避免要求用户重复提供可自动发现的信息。

### 虚拟化环境只读检查

- 当前系统版本不提供可直接使用的 Hyper-V、Virtual Machine Platform、Windows Hypervisor Platform 或 Windows Sandbox 功能入口；本机也未安装 VMware、VirtualBox 或 QEMU。
- `HypervisorPresent=True`，与已开启的 VBS/HVCI 基线一致；传统 `VirtualizationFirmwareEnabled`/SLAT 字段在虚拟机管理器已接管硬件时不能据此判断 BIOS 未开启虚拟化。
- 当前设备为 MECHREVO Yaoshi16Super Series GM6IX8B，Intel Core i9-14900HX；硬件能力足以支持 Windows 11 x64 虚拟机，实际兼容性以安装后的启动测试为准。
- 厂商当前说明：VMware Workstation Pro 26H1 支持 Windows 11 客体，并对个人、教育和商业使用免费；新版可在 Windows 主机启用 VBS/Hyper-V 层时运行，但主机 VBS 与客体嵌套 VBS 不能同时作为受支持组合。
- 因此桌面虚拟机只用于测试签名、驱动功能、Typeless 识别、安装/卸载和快照恢复；不能替代正式的 HVCI Readiness 与 HLK 认证环境。

### 发现无需自研驱动的系统扫描码映射候选

- 用户指出 Typeless 的“添加另一个”入口可以保留现有右 `Ctrl + 右 Shift`，再增加一个冷门键作为第二套语音输入快捷键，并建议把遥控器原始 F5 替换为 `PrtSc` 或小键盘键。
- 界面语义澄清：“添加另一个”用于增加另一套可独立触发语音输入的快捷键，不是向现有组合追加第三个键。
- 本地只读检查 Typeless 2.3.1 键名表：明确包含 `F13` 到 `F20`、Print Screen 和小键盘键；因此无需假设 Typeless 只能接受普通键位。
- 候选优先级：推荐 `F13`。`PrtSc` 在 Windows 11 上通常关联截图功能，不够冷门；小键盘键可能影响数字输入；F13 几乎没有默认系统行为。
- Windows API 核对：F5 扫描码为 `0x3F`，F13 为 `0x64`，Print Screen 为 `0x0054`，Numpad9 为 `0x49`。
- 注册表只读检查：`HKLM\SYSTEM\CurrentControlSet\Control\Keyboard Layout` 当前不存在 `Scancode Map`，没有用户既有映射需要合并或保护。
- 可行实验：使用系统级 `Scancode Map` 把 F5 映射为 F13，重启后在 Typeless 中将 F13 添加为第二套语音快捷键，再验证遥控器按住/松开、记事本无时间插入和实体键盘行为。
- 显式代价：该机制按扫描码全局生效，不能区分小米遥控器与电脑键盘；电脑实体 F5 也会变成 F13。启用和撤销都需要重启。
- 安全与回退：在用户明确接受全局 F5 代价前不写注册表；若实验成功，虚拟 HID/WDK/正式驱动签名路线可从 v0.1 移出并保留为未来设备级高级方案；若失败，则恢复注册表并回到 D-014。

### 用户批准 F5 → F13 可逆实验

- 用户选择选项 1，明确接受电脑键盘和遥控器的实体 F5 都全局变成 F13，以及启用/撤销均需重启的代价。
- 新增 `tools/keyboard-remap`：启用、撤销和状态检查脚本；所有写操作均做精确值比对和回读验证。
- 新增 `docs/F5_TO_F13_EXPERIMENT.md`：记录启用、重启、Typeless 配置、端到端测试与回滚步骤。
- 虚拟机、WDK、虚拟 HID 和正式签名工作立即暂停，等待本次轻量方案的实体测试结果。
- 不由自动化直接重启日用电脑；完成注册表写入后由用户选择合适时机重启。

### F5 → F13 映射写入成功

- 三个 PowerShell 工具均通过语法解析检查，0 个解析错误。
- 写入前状态脚本确认不存在 `Scancode Map`。
- 第一次普通权限执行被 `#Requires -RunAsAdministrator` 正常阻止，系统仍保持未修改状态；随后通过可见 UAC 获取管理员权限。
- 管理员脚本退出码为 0；普通会话回读确认当前注册表值与 `F5 (0x3F) → F13 (0x64)` 的预期 20 字节映射完全一致。
- 没有自动重启电脑。映射需在下一次 Windows 启动后生效；下一步只验证 Typeless 能否把遥控器麦克风键保存为 F13。

### 重启后 Typeless 未识别 F13

- 用户反馈 Typeless 没有识别到遥控器麦克风键。
- 系统状态复核：Windows 最近启动时间为 2026-08-20 02:56:02，证明映射写入后确实完成了重启；注册表仍为预期 20 字节 `F5 → F13` 值。
- 第一次 30 秒监听只捕获到一串普通字母键，没有 F5/F13；第二次短对照监听没有捕获到事件。两轮都未形成“电脑 F5 与遥控器麦克风键”的有效对照，因此不能据此断言扫描码映射机制失效。
- 下一步改用无倒计时的记事本对照：分别按电脑实体 F5 与遥控器麦克风键，利用记事本 F5 插入时间的可见行为判断两条输入路径是否经过映射。
- 在对照结果明确前暂不撤销注册表，也不恢复虚拟 HID 开发。

### 记事本 F5 对照结果

- 用户确认：电脑实体 F5 不再插入时间；遥控器麦克风键也不再插入时间。
- 该结果证明原有 F5 系统副作用已同时消失，但仅凭“没有插入时间”不能区分输入被转换为 F13、被设备固件暂时抑制或被目标应用忽略。
- 后续 60 秒 Raw Input/翻译层监听仍只记录到普通字母、Shift 和 Enter 等键，没有形成 F5/F13 对照；这些事件来自 `VID_36B0&PID_3002`，且探针标记 `target=False`，不能当作小米遥控器麦克风键证据。
- 停止继续使用隐藏倒计时监听。下一项最小测试改为：在 Typeless 同一个“添加另一个”快捷键录入框中按电脑实体 F5。若显示 F13，说明系统映射与 Typeless F13 支持均成立，问题只在遥控器路径；若仍无反应，则 F13 不能作为当前 Typeless Windows 捕获器的快捷键。

### Typeless F13 方案判定失败

- 用户在 Typeless 的“添加另一个”录入框中按电脑实体 F5，界面完全没有反应。
- 这排除了“仅遥控器路径异常”：系统注册表映射存在且原始 F5 副作用已消失，但 Typeless Windows 捕获器没有把映射后的 F13 作为可录入按键。
- F13 候选停止继续调试。当前注册表暂不重复修改，先对用户原建议中的小键盘键进行物理预检。
- 首选预检键为小键盘乘号 `Numpad *`：不受 NumLock 开关改变，在 Typeless 本地键名表中存在，且比数字键更少影响普通输入。
- 若物理 `Numpad *` 能被 Typeless 保存，再把全局 F5 目标从 F13 改为 Numpad Multiply；若物理键本身也不被接受，则撤销 F13 映射并结束这条轻量路径。

### 选择 Numpad Divide 作为新映射目标

- 用户确认 Typeless 能识别 Numpad 系列实体键，并选择使用频率更低的除法键而非乘法键。
- Windows API 核对：`VK_DIVIDE (0x6F)` 的扩展扫描码为 `0xE035`；新映射条目为目标 `35 E0`、来源 F5 `3F 00`。
- 删除仅适用于失败 F13 实验的旧脚本，新增 Numpad Divide 启用/迁移脚本、可识别两种项目值的撤销脚本与状态脚本。
- 新增 `docs/F5_TO_NUMPAD_DIVIDE_EXPERIMENT.md`；原 F13 文档保留并标记失败，作为完整过程记录。

### F5 → Numpad Divide 迁移写入成功

- 新版启用、撤销和状态脚本均通过 PowerShell 语法解析，0 个解析错误。
- 迁移前状态脚本准确识别出旧 `F5 → F13` 项目实验值并返回专用状态码 1。
- 通过可见 UAC 运行管理员迁移；脚本只允许替换该精确旧值，管理员进程退出码为 0。
- 迁移后普通会话回读确认当前值为 `00000000000000000200000035E03F0000000000`，即 `F5 (0x003F) → Numpad Divide (0xE035)`；状态码为 0。
- 没有自动重启。当前登录会话仍使用上次启动时加载的旧映射；下一次 Windows 启动后验证遥控器麦克风键是否被 Typeless 识别为 Numpad Divide。

### Typeless 成功识别遥控器为 NumpadDivide

- 用户重启 Windows 后，在 Typeless → 设置 → 键盘快捷键 → 语音输入中按遥控器麦克风键。
- Typeless 成功显示并保存 `NumpadDivide`，没有出现系统保留键错误。
- 截图确认语音输入当前包含两套独立入口：`NumpadDivide` 与右 `Ctrl + 右 Shift`；原键盘兜底快捷键未被删除。
- 结论：`F5 → Numpad Divide` 系统扫描码方案解决了 F5 时间插入副作用和 Typeless 拒绝模拟输入问题；虚拟 HID、WDK、虚拟机与正式驱动签名继续暂停。
- 尚待最终验收：关闭 Typeless 设置，在普通文本框聚焦时运行 `--voice-live`，按住遥控器麦克风说话并松开，确认 Typeless 自动开始/停止并输出遥控器音频转写文本。

### 重启后 BLE 名称变化与 PowerShell 管道乱码

- 第一次 Numpad Divide 端到端命令在查找设备阶段退出；屏幕中的中文目标名同时显示为乱码。
- 只读系统枚举确认遥控器仍存在且状态为 OK：顶层名称变为固件短名 `MI RC`，对应小米硬件标识 `VID 2717 / PID 32B8`，蓝牙地址尾部为 `C05D39C2CCAA`。
- 根因一：探针只对默认中文名“小米蓝牙语音遥控器”做完全相等匹配；Windows 重启后暴露 `MI RC`，因此没有命中。不是配对丢失。
- 根因二：探针已设置 UTF-8 输出，但 Windows PowerShell 5.1 的原生程序管道按旧控制台代码页解码，导致 `| Tee-Object` 中的中文变为 mojibake。该显示问题不改变程序内部 Unicode 字符串。
- 修复：默认设备名集合同时接受中文名与 `MI RC`；用户显式传入 `--name` 时仍只接受该名称。当前复测暂不使用 PowerShell 5.1 的 `Tee-Object` 管道，把编码问题与蓝牙连接验证分开。

### MI RC 别名修复验证成功

- 修复后 Debug 构建成功：0 个警告、0 个错误。
- 运行 2 秒 `--voice-live` 冒烟测试且不经过 PowerShell 管道，控制台正确显示默认候选“小米蓝牙语音遥控器 | MI RC”。
- 程序成功连接 `name=MI RC status=Connected`，发现 9 个 GATT 服务，ATVV CONTROL/AUDIO 订阅成功，能力协商返回 codec `0x02`、frame size 120。
- VB-CABLE 以 16 kHz、单声道、16-bit 共享模式成功打开并显示 `Live route armed`。
- 本轮刻意没有按实体麦克风键，因此 2 秒后显示“没有收到音频包”是预期结果；连接、名称兼容、ATVV 与虚拟音频初始化均已通过。
- 下一步重新执行 45 秒端到端测试，不使用 `Tee-Object`；用户在记事本聚焦时按住遥控器麦克风说话并松开。

### NumpadDivide 端到端测试暴露手势冲突

- 用户观察：长按遥控器麦克风键时 Typeless 弹出快捷键帮助层；短按能启动 Typeless，但只有刚开始出现麦克风电平，最终输入为空。
- 控制日志与文件验证音频链路正常：1,165 包、139,800 字节 ADPCM、279,600 个 PCM sample，约 17.475 秒；生成 559,244 字节 WAV。
- 第一段长按约 3.25 秒，第二段约 8.76 秒，末段约 4.9 秒，均持续收到音频；Typeless 因长按进入快捷键提示而没有转写。
- 多次短按产生约 0.15–0.18 秒的 HTT 音频段；Typeless 在松开后切换为录音，但此时遥控器固件已经关闭隐私门，所以只看到瞬时电平且没有文本。
- 结论：不是编码、设备名称、音量或 VB-CABLE 故障，而是 Typeless 的“轻触切换”与遥控器的“保持按下才采音”在时间上互斥。
- 进入新的基础产品取舍：无驱动双按钮工作流，或恢复虚拟 HID 来保持原始一键按住说话目标。

### 用户选择无驱动的分离式语音工作流

- 用户选择方向 1，不恢复当前阶段的虚拟 HID/WDK/签名工程。
- Typeless：轻触开关键开始，按住麦克风说话，再轻触开关键完成并转写。
- Codex Voice：轻触 TV 键打开 Voice；之后按住麦克风说话、松开结束音频段，Voice 保持可快速接续对话。
- 麦克风键从“应用快捷键”降为单一职责的硬件隐私门；当前 `F5 → NumpadDivide` 不能保留，否则长按仍会触发 Typeless 快捷键帮助层。
- 为减少重启次数，暂不立即把 F5 改回安全 sink；先确定开关键是否向 Windows 发送可重映射的键盘扫描码，再一次性写入最终扫描码表。
- 下一项只确认安全事实：用户是否已经验证轻触开关键不会让电脑睡眠、锁屏或关机。确认后再进行针对性采样。

### 开关键安全采样成功并形成最终双按键映射

- 用户确认曾轻触开关键且 Windows 完全没有反应，不会睡眠、锁屏、关机或弹出系统界面。
- 运行 30 秒只读 Raw Input/翻译层监听，仅轻触一次开关键；没有执行快捷键、注册表写入或系统动作。
- 捕获结果：翻译层与小米目标设备层均报告扩展扫描码 `E0 5E`、`VK 0xFF`；key-down/key-up 成对，目标设备为 `VID 2717 / PID 32B8`。
- 最终映射脚本改为两条扫描码一次写入：开关键 `E0 5E → E0 35 NumpadDivide`，原始 F5 `00 3F → 00 64 F13`。
- 旧 `F5 → NumpadDivide` 工具被分离式语音工具取代；保留旧实验文档并标记结果，确保第一次开源项目的技术取舍可追溯。
- 工具继续坚持精确值保护、写后回读和可逆撤销；不会覆盖用户或第三方的未知 Scancode Map。
- 尚未自动重启。下一步先完成脚本语法、字节值和当前注册表迁移状态校验，再由用户决定重启时机。

### 最终双按键映射写入成功

- `Enable-SplitVoiceRemap.ps1`、`Disable-VoiceRemap.ps1`、`Get-VoiceRemapStatus.ps1` 均通过 PowerShell AST 解析，0 个语法错误。
- 写入前状态脚本准确识别当前值为项目旧的 `F5 → NumpadDivide` 实验映射，符合唯一允许的自动迁移条件。
- 通过可见 UAC 执行管理员迁移，脚本退出码为 0；未自动重启 Windows。
- 普通权限回读确认最终值为 `00000000000000000300000035E05EE064003F0000000000`：开关键 `E0 5E → E0 35 NumpadDivide`，F5 `00 3F → 00 64 F13`。
- 旧实验文档已改为历史归档说明，不再引用已删除的工具命令；工具目录只保留最终启用、状态、撤销三个脚本。
- 下一步：用户在合适时机手动重启，然后先验收 Typeless 三步流程；通过后再实现和验证 TV 键打开 Codex Voice。

### 重启后第一次分离式语音验收未形成有效样本

- Windows 已于 2026-08-20 23:12:26 重新启动；状态脚本确认最终双按键映射仍准确配置。
- Typeless 2.3.1 已从正式安装路径启动，测试期间存在正常的 Electron 主进程与辅助进程。
- 第一次把测试时长误设为 90 秒，程序按 `--voice-live` 最大 60 秒的参数保护立即拒绝运行；没有半启动状态或系统修改。随后改为合法的 60 秒。
- 60 秒测试成功连接 `MI RC`，完成 ATVV 能力协商并打开 VB-CABLE 16 kHz/mono/16-bit 实时输出；期间仅收到连接维持响应，没有收到任何 ATVV 音频包。
- 该结果不能单独判定最终映射失败：还需用户确认开关键是否切换 Typeless、是否在监听窗口内持续按住麦克风，以及本地界面具体表现。
- 用户随后确认测试期间短暂离开，没有看到操作提示，也没有在 60 秒窗口内执行遥控器动作；因此“0 个音频包”是无操作的预期结果，不计为失败样本。
- 下一轮不直接启动倒计时；先让用户确认准备完成，再分别观察“开关键切换”和“按住麦克风采音”，缩小故障范围。

### 第二次分离式语音验收成功捕获完整音频

- 用户明确回复“准备好了”后才启动第二次 60 秒 `--voice-live`，输出保存为 `logs/split-voice-e2e-second.wav` 与同名 `.adpcm`。
- 程序成功连接 `MI RC`、完成 ATVV 能力协商、打开 VB-CABLE，并在用户按住麦克风期间持续接收音频。
- 最终统计：625 个 ATVV 音频包、75,000 字节 ADPCM、150,000 个 PCM sample；按 16 kHz 计算约 9.375 秒，增益 `+6 dB`。
- 观察到完整的麦克风开启/结束控制过程，说明重启后的 `F5 → F13` 安静占位映射没有破坏遥控器硬件隐私门和 ATVV 音频传输。
- BLE、ATVV、ADPCM 解码、增益和 VB-CABLE 客观链路通过；Typeless 是否由两次开关键正确开始/结束并在记事本生成文字，等待用户确认。

### 定位并修复跨麦克风段 ADPCM 状态漂移

- 用户确认两次开关键都能让 Typeless 开始/结束，但没有正确文字；第一次只有短暂电平波动，第二次没有有效输入。
- 对第二次 WAV 做只读统计：9.375 秒、峰值 `0 dBFS`、RMS `-0.37 dBFS`、99.29% 样本高于约 `-50 dBFS`，与正常人声不符。
- 历史 WAV 对照显示：用户确认清晰的 `atvv-second-voice.wav` 与 `vb-cable-live-test.wav` RMS 分别约 `-38.07`、`-34.37 dBFS`；后续多段实时文件均异常接近满幅。
- 根因：旧实现让 IMA ADPCM 解码器跨多个独立实体麦克风段持续沿用 predictor/index，并会接收初始化完成前的旧流中段包；一旦从错误状态起步，后续 PCM 会长期饱和。
- 修复：增加初始化门控、实体段活动状态、每段 decoder reset、分段 PCM 队列，并区分 `00 02` 实体段结束与 `00 00` 会话关闭。
- 修复后 Debug 构建成功：0 个警告、0 个错误。下一项验收同时检查 Typeless 转写和 WAV RMS 是否恢复到合理人声范围。

### 分段解码修复后实体音频恢复正常动态范围

- 用户明确准备完成后启动 60 秒复测，并在 `Capture armed` 后执行开关键开始、按住麦克风说话、松开、开关键结束。
- 控制日志捕获 `04 03 02 23` 新实体段开始；程序打印 decoder reset。该段约 6.435 秒，随后收到 `00 02` 正常结束。
- 最终统计：429 包、51,480 字节 ADPCM、102,960 个 PCM sample、1 个独立段、0 个初始化前/非活动段丢弃包。
- 修复后 WAV 峰值 `-19.80 dBFS`、RMS `-46.50 dBFS`、高于约 `-50 dBFS` 的样本占 28.95%；相比修复前 RMS `-0.37 dBFS` 和 99.29% 活跃样本，严重削顶已消失。
- 当前剩余判断：本次人声平均音量比两段历史清晰样本（约 `-38`、`-34 dBFS`）更低；先等待 Typeless 是否产生文字，再决定提高默认增益或保持 +6 dB。

### Typeless 首次输出正确文字，继续验证尾部完整性

- 用户确认修复后 Typeless 输出了完整且正确的测试句，双按钮分离式流程第一次端到端通过。
- 用户主观感觉麦克风结束偏早，担心正确句子存在 Typeless 智能补写贡献。
- 对 0.25 秒音量包络的只读分析显示：结尾 `6.25–6.435s` RMS 约 `-60.21 dBFS`，最后一段已接近静音；没有观察到在强语音能量上直接硬切的证据。
- 下一轮采用不可预测短语，并明确要求说完后继续按住 0.5 秒、松开后等待 2 秒再轻触结束，区分遥控器语音段尾部与 Typeless/VB-CABLE 消化延迟。
- 本次峰值仅 `-19.80 dBFS`，仍有充足余量；下一轮先用命令行临时 `--gain-db 12`，不立即修改默认值，观察识别稳定性后再决定。

### +12 dB 不可预测句子复测保留完整自然尾部

- 用户准备完成后，以临时 `--gain-db 12` 启动 60 秒测试；测试句尾包含不可由常规上下文稳定猜出的数字“七三九五”。
- 用户按计划说完后继续按住约 0.5 秒，松开后等待约 2 秒，再轻触开关键结束 Typeless。
- 程序捕获 1 个独立段、579 包、69,480 字节 ADPCM、138,960 个 PCM sample（8.685 秒），0 个初始化前/非活动段污染包。
- WAV RMS `-36.19 dBFS`，回到两段历史清晰录音的 `-38.07/-34.37 dBFS` 区间；触顶 19 个样本，占 0.0137%，没有实质削顶风险。
- 最后 0.5 秒 RMS `-58.26 dBFS`、峰值 430，证明录音在自然低能量尾部后结束，没有在强语音中硬切。
- 等待用户确认 Typeless 是否完整输出不可预测句子及末尾“七三九五”；若通过，可把默认实时增益正式改为 +12 dB，并把 0.5 秒/2 秒节奏写入首发交互说明。

### 双探针验证前后自然无等待交互

- 用户要求把“开关键到麦克风”和“麦克风到结束开关键”两个延迟都降到最低，并使用难以猜测的复杂句验证。
- 同时启动 90 秒 Raw Input/翻译键盘探针和 60 秒 ATVV/VB-CABLE 音频桥；用户不故意等待，测试句包含随机数字 `8417` 和尾词“山竹”。
- 第一次开关键：`00:08:38.384` down、`00:08:38.441` up；ATVV 实体麦克风开始 `00:08:38.606`。按开关键松开计算，前置自然间隔约 165 ms。
- 实体麦克风结束 `00:08:47.457`，F13 key-up `00:08:47.458`；第二次开关键 `00:08:47.697` down、`00:08:47.817` up。后置自然间隔约 239–240 ms。
- Typeless 在 `00:08:50.283` 开始注入粘贴动作，即第二次开关键松开后约 2.47 秒；识别处理与用户交互解耦。
- 音频统计：593 包、71,160 字节 ADPCM、142,320 个 PCM sample、8.895 秒、1 段、0 个污染包；`+12 dB` 后 RMS `-36.08 dBFS`、峰值 21,852、0 个触顶样本。
- Typeless 完整输出：“琥珀色齿轮 26 号，3 只白鹭绕过月球车，校验码是 8417，最后一个词是山竹”。用户评价识别效果很好。
- 末尾 250/500 ms 仍有可测能量；用户说明周围手机正在公放，因此该能量不能作为截断证据。完整随机尾词已被正确识别。
- 结论：桥已预热时，两个用户侧固定等待均可取消；自然动作约 0.2 秒已经通过。程序默认增益正式改为 +12 dB，保留命令行可调能力。

### 启动 Codex Voice 与多对话切换阶段

- 用户确认 Typeless 复杂句识别效果很好，批准进入下一步，并提出桌面端不同对话切换适合多项目 Vibe Coding。
- 先实现无系统修改的 `--codex-voice-shortcut-test`：倒计时 5 秒后发送一次扫描码级 `左 Ctrl + 反引号`，验证当前 Codex 是否接受程序注入。
- TV 实体键与普通键盘反引号共用 scan `0x29`；在完成设备隔离前，不使用会同时破坏实体键盘反引号的全局扫描码映射。
- 官方 OpenAI 文档未提供可依赖的公开上一/下一对话快捷键或外部任务导航 API。后续原型采用 Windows UI Automation，并限制为目标 Codex/ChatGPT 前台窗口；目标不确定时安全失败。
- 多项目交互候选：菜单键进入会话切换层，方向上/下选择，确认进入；待 Voice 端到端链路通过后再确认是否占用这些手势。

### Codex Voice 快捷键通过并合并实时语音桥

- 用户运行快捷键探针后确认 Codex Voice 成功打开；扫描码级 `左 Ctrl + 反引号` 注入通过。
- 用户随后发现遥控器语音尚不能输入。原因是该探针有意只验证快捷键，没有启动 BLE、ATVV 解码或 VB-CABLE。
- 新增 `--codex-voice-live`：先完成遥控器连接、ATVV 协商、ADPCM 解码器与 `CABLE Input` 输出初始化；提示用户切回 Codex 5 秒后打开 Voice，再接收实体麦克风按住期间的音频。
- Typeless 与 Codex Voice 保持两套清晰流程：前者由开关键开始/结束并负责转写；后者直接从 `CABLE Output` 接收语音，不要求 Typeless 参与。
- 合并模式构建通过：0 个警告、0 个错误。下一项实体测试需确认 Codex Voice 实际选择的录音设备为 `CABLE Output`。

### Codex Voice 端到端通过并进入 TV 实体键测试

- 首次合并测试已收到 3 个实体麦克风段和音频包，但 Voice 没有回复；日志证明 BLE/ATVV/VB-CABLE 已工作，故障边界收敛到 Codex 使用的录音端点。
- Windows 端点枚举确认 `CABLE Output`、`CABLE Input` 与 Realtek 麦克风同时存在。用户将 `CABLE Output` 设为默认录音设备和默认通信设备后复测。
- 30 秒复测捕获 1 个完整段、416 包、49,920 字节 ADPCM、99,840 个 PCM sample（约 6.24 秒）、0 个污染包，增益 `+12 dB`；WAV 与 ADPCM 均完整保存。
- 用户确认 GPT 听懂并正确回复。`TV/快捷键 → ATVV → ADPCM → VB-CABLE → Codex Voice → 回复` 除 TV 实体触发外的整条链路通过。
- 新增 `--tv-voice-test`：在 5–60 秒受控窗口内临时把物理 scan `0x29` 转成 `Ctrl + 反引号`，退出即解除，不写注册表、不要求重启。
- 已知实验边界：Windows 低级键盘钩子不携带设备 ID，因此测试期间电脑实体反引号也暂时触发 Voice；正式常驻版必须增加 Raw Input 设备归属与安全回放，或改用不会破坏常用键的交互入口。

### 第一版 TV 设备隔离路由失败并增加诊断

- 短时全局 TV 测试先通过：一次按下/松开得到 `blockedEvents=2`、`voiceToggles=1`，Codex Voice 成功打开。
- 第一版设备感知路由测试中，普通键盘反引号没有回放，遥控器 TV 也没有触发 Voice；退出统计为 `blockedEvents=12`、`remoteEvents=0`、`voiceToggles=0`、`keyboardReplays=0`。
- 安全性符合预期：30 秒结束后低级钩子和 Raw Input 窗口均释放，没有注册表修改，也没有要求重启。
- 故障边界：低级钩子收到物理事件，但 Raw Input 路由没有形成可分类的 scan `0x29` 事件；不能把该版本合入常驻桥。
- 修正版增加前 20 个 Raw Input 键盘事件的设备名、VK、scan 和状态日志，增加 WndProc 异常日志，并允许用 `VK_OEM_3 0xC0` 作为 scan `0x29` 的兼容判定。构建通过，等待短时诊断复测。
- 第二次诊断仍为 `blockedEvents=4`、`rawKeyboardEvents=0`；用户正确指出中英文输入法与 Voice 全局快捷键无关。该轮排除键码和输入法因素。
- 与已验证 `MiVibe.Remote.Probe` 对照发现：探针在标记 `[STAThread]` 的主线程创建 Raw Input 消息窗口并运行 `Application.Run()`；失败版则把窗口放在独立后台 STA 线程。
- 修复：为 GATT 探针入口补充 `[STAThread]`，并让 `--tv-voice-test` 在任何 `await` 之前直接于主 STA 线程运行 Raw Input 窗口、低级抑制钩子和 WinForms 定时器。后台路由版本不进入当前验收路径。
- 主 STA 修正版构建通过：0 个警告、0 个错误；等待同一组普通键盘反引号与遥控器 TV 对照复测。
- 主 STA 复测结果仍为 `blockedEvents=4`、`rawKeyboardEvents=0`；因此后台线程不是根因，输入法也与全局 Voice 快捷键无关。
- 结合三轮一致结果确认：低级钩子抑制物理 `OEM_3` 后，同一事件不再进入 Raw Input；程序无法在抑制之后取得设备 ID。若取消抑制，则反引号已经进入前台应用，无法安全地事后撤回。
- 判定当前设备感知 TV 用户态实验失败，停止继续用键码、输入法或消息线程做无效迭代。短时钩子均已自动解除，没有系统残留。
- 下一产品决策：无驱动 v0.1 改用菜单键触发 Voice（推荐）、接受运行期间占用实体反引号，或提前启动需签名的 HID filter 路线。

### 选择 TV 全局映射作为 v0.1 最短交付路径

- 用户选择候选 2：继续使用 TV 键控制 Codex Voice，并接受 MiVibe 运行期间电脑实体反引号也触发 Voice。理由是 Voice 对话期间打字概率较低，尤其在写代码的语音交互中；v0.1 先获得基本功能，后续再做设备级优化。
- 删除未通过实体测试的 Raw Input 设备感知回放实现，恢复已成功打开 Voice 的低级键盘钩子路径，避免把不可工作的实验代码带入常驻语音桥。
- 新增 `--tv-codex-voice-live [sec]`：完成 BLE 连接、ATVV 协商、分段 ADPCM 解码、`+12 dB` PCM 和 VB-CABLE 输出预热后，启用 TV/反引号到 `Ctrl + 反引号` 的临时控制。
- 防抖规则：一次物理按下只触发一次 Voice 切换；按住产生的自动重复事件全部抑制；物理松开后才允许下一次切换。程序注入的快捷键带 injected 标记，不会被钩子递归处理。
- 生命周期：正常结束、异常清理或用户中止时都会解除钩子；不写扫描码注册表、不要求重启。控制台会明确打印实体反引号已恢复。
- README、开发计划与 D-025 已同步记录 v0.1 限制和未来 HID filter/正式驱动签名路线。
- Debug 构建通过：0 个警告、0 个错误。下一步执行 45 秒实体端到端验收：TV 打开 Voice、按住麦克风说复杂随机句、松开并确认 GPT 正确回复，再用 TV 关闭 Voice。

### TV 控制的 Codex Voice 完整链路通过实体审核

- 用户运行 `--tv-codex-voice-live 45`，程序成功连接 `MI RC`，ATVV 1.0 能力协商返回 16 kHz codec `0x02`、frame size 120，并打开 `CABLE Input` 实时输出。
- 第一次 TV 轻触成功发送 `Ctrl + 反引号` 并打开 Codex Voice；实体麦克风产生 1 个独立 HTT 段，decoder 按段重置。
- 音频统计：675 包、81,000 字节 ADPCM、162,000 个 PCM sample，约 10.125 秒；1 个有效段、9 个预热/非活动包被安全丢弃，增益 `+12 dB`。
- 第二次 TV 轻触成功关闭 Voice；钩子统计 `blockedEvents=4`、`voiceToggles=2`，说明两次按下/松开各触发一次切换，没有自动重复误触发。
- 程序正常取消 AUDIO/CONTROL 订阅，并打印 `Physical backtick is restored`。没有注册表残留，也不需要重启。
- 用户确认“整个链路成功”：GPT 已听懂并正确回复。由此正式通过 `TV → Codex Voice → 实体麦克风 → BLE ATVV → ADPCM → +12 dB PCM → VB-CABLE → GPT 回复 → TV 关闭 → 按键恢复` 的 v0.1 端到端验收。
- 下一阶段从硬件/语音可行性研究转入可日常运行的后台常驻 MVP：统一 Typeless 与 Codex Voice 两条工作流、基础映射配置、托盘暂停/退出、异常恢复与日志导出。

### 常驻语音核心与 AirPods 路由体检完成第一版

- 用户批准进入下一阶段，并提出音频策略：小米遥控器优先作为语音输入；检测到 AirPods 时让其承担音频输出。
- 设计澄清：Windows 语音应用实际看到的遥控器输入端点是 `CABLE Output`，MiVibe 向 `CABLE Input` 写入 PCM；AirPods 不应成为 capture 默认设备，只应作为 render 输出。
- 新增 `--audio-status`：只读输出 multimedia/communications 两个角色的默认录音与播放端点，检测活动 AirPods render/capture endpoint，并对 `CABLE Output` 输入优先级、AirPods 默认输出和 AirPods 麦克风抢占分别给出 PASS/INFO/WARNING。
- 本机检测结果：两个默认录音角色均为 `CABLE Output (VB-Audio Virtual Cable)`，输入优先级通过；当时没有活动 AirPods endpoint；默认播放分别为 Realtek 扬声器与 DELL 显示器音频。
- 新增 `--resident`：不设固定运行时长，保持 ATVV 与 VB-CABLE 语音桥，开关键继续服务 Typeless、TV 键服务 Codex Voice、实体麦克风按住时提供共同音频输入。
- 常驻模式不再把每个 ADPCM/PCM 包加入保存队列，只保留累计计数，避免长时间运行内存线性增长；显式测试模式保留 WAV/ADPCM 证据保存。
- 安全冒烟测试成功连接 `MI RC`、完成 ATVV 1.0 协商并启动 16 kHz/mono/16-bit VB-CABLE 输出；静默运行后以 `Ctrl+C` 退出。
- 清理日志确认：发送 `MIC_CLOSE 0D00`，收到 `CONTROL 0000`，AUDIO/CONTROL 取消订阅成功，TV 钩子 `blockedEvents=0 voiceToggles=0`，实体反引号恢复。静默统计 0 包、0 段符合预期。
- Debug 构建通过：0 个警告、0 个错误。下一项验收是连接 AirPods 后复查路由，并在常驻模式中完成一次 Typeless 转写和一次 Codex Voice 问答，同时确认 GPT 回复从 AirPods 播放。

### AirPods 首次并行路由体检发现通信输入抢占

- 用户连接 AirPods Pro 后运行 `--audio-status`；程序成功识别活动 AirPods 播放端点。
- 默认播放结果正确：multimedia 与 communications 两个输出角色均为 AirPods Pro。
- 默认录音结果只通过一半：multimedia 输入为 `CABLE Output`，但 communications 输入为 AirPods Pro。
- 风险：Codex Voice 若使用 communications capture role，会直接打开 AirPods 麦克风，绕开小米遥控器/VB-CABLE 输入，并可能触发蓝牙语音配置切换。
- 修正目标：把 `CABLE Output` 同时设为“默认设备”和“默认通信设备”；AirPods Pro 继续保留为两个播放角色的默认设备，不需要禁用 AirPods 麦克风。
- 控制台中的中文“耳机”出现乱码是 Windows PowerShell 5.1 管道解码的显示问题；程序仍通过名称中的 `AirPods Pro` 正确完成端点判断，不影响路由结论。

### AirPods 与遥控器输入角色修正通过

- 用户将 `CABLE Output` 同时设为默认录音设备和默认通信录音设备后再次运行 `--audio-status`。
- 两个输入角色均为 `CABLE Output (VB-Audio Virtual Cable)`，程序判定小米遥控器语音桥具有输入优先级。
- 两个输出角色均为 AirPods Pro，程序成功识别活动 AirPods 播放端点并判定其为默认输出。
- AirPods 麦克风端点仍存在，但不再承担任何默认输入角色；路由体检三项均为 PASS。
- 设置层面的输入抢占已排除。下一步在 `--resident` 常驻模式内连续完成 Typeless 转写与 Codex Voice 问答，观察 BLE 共存稳定性并确认 GPT 回复从 AirPods 播放。

### AirPods 与小米遥控器常驻并行验收通过

- 用户保持 AirPods Pro 连接并运行 `--resident`，在同一常驻会话内依次完成 Typeless 复杂句转写和 Codex Voice 问答。
- Typeless 完整正确输出：“银灰色灯塔 72 号，4 只松鼠穿过木星隧道，校验码是 6158，最后一个词是柚子”，包括随机数字和不可预测尾词。
- Codex Voice 听懂问题并正确回答其识别到的“7 乘以 6”为 42；原句“17”被识别为“7”，记录为一次词级 ASR 偏差，不判定为传输故障。
- GPT 回复声音确认从 AirPods Pro 播放。用户听到少量电音，但此前未连接本次新链路时也存在相同现象，现有证据不支持将其归因于 AirPods/遥控器蓝牙共存。
- 客观统计：2 个独立实体麦克风段、851 个音频包、102,120 字节 ADPCM、204,240 个 PCM sample，合计约 12.765 秒；4 个非活动包被安全丢弃，增益 `+12 dB`。
- 两次 TV 切换得到 `blockedEvents=4 voiceToggles=2`；退出发送 `MIC_CLOSE 0D34`，收到 `CONTROL 0000`，AUDIO/CONTROL 取消订阅成功，反引号恢复。
- 结论：`AirPods 输出 + 小米遥控器 BLE ATVV 输入 + Typeless + Codex Voice` 的常驻并行链路通过 v0.1 实体验收。单次“十七→七”不阻塞托盘 MVP；后续稳定性测试统计 ASR 偏差率，而不以单句要求绝对零错误。

### 托盘 MVP 完成并通过安全停止测试

- 新增 `MiVibe.Remote.Tray` WinForms/WinExe 项目；启动时自动运行隐藏的 GATT `--resident` 子进程，不显示控制台窗口。
- 托盘右键菜单提供状态、启动、暂停、检查音频路由和安全退出；双击图标可直接查看四个默认音频角色及 PASS/WARNING。
- 新增内部 `--shutdown-event`：托盘用唯一命名事件请求 resident 正常停止，避免 Pause/Exit 使用强制杀进程。
- 托盘实时解析后台输出：出现 `Resident bridge armed` 后状态变为“运行中”；目标设备找不到或 ATVV 失败时显示失败状态；完整输出按 UTF-8 保存到应用输出目录的 `logs/tray-*.log`。
- 第一次构建发现托盘默认 `windows7.0` API 面与 BLE 核心 `windows10.0.26100.0` 不兼容；统一为 Windows 11 SDK 目标后构建通过。
- 修复退出事件与暂停等待可能同时释放 Process 的极端竞态；停止期间由发起路径独占清理，意外退出才由 Exited 回调清理。
- 两轮自动启动/退出测试通过。最终 8 秒测试：托盘退出码 0、残留 MiVibe 进程数 0，日志包含 `Resident bridge armed`、`MIC_CLOSE`、AUDIO/CONTROL unsubscribe 和反引号恢复。
- GATT 核心与托盘最终构建均为 0 个警告、0 个错误。下一步由用户首次可见启动托盘，验证通知图标、音频体检弹窗、暂停/恢复和正常遥控器使用。

### 托盘 MVP 首次可见实体验收通过

- 用户从开发构建启动 `MiVibe.Remote.Tray.exe`，完成托盘操作并成功使用 TV 键打开 Codex Voice。
- 托盘“音频路由”弹窗正确显示中文端点名称，不再出现 Windows PowerShell 5.1 管道造成的乱码。
- 弹窗显示两个输入角色均为 `CABLE Output`、两个输出角色均为 AirPods Pro；遥控器输入优先、AirPods 输出、AirPods 麦克风非默认三项均为 PASS。
- 结论：托盘 UI、隐藏 resident 子进程、UTF-8 诊断、音频路由检测与 TV Voice 实际动作全部通过首次用户验收。托盘 MVP 阶段完成。
- 下一阶段建议按风险顺序实现：蓝牙断线自动重连与状态反馈、日志轮转、开机启动开关、正式应用图标与可发布打包；完整按键映射 UI 随后推进。

### 冻结 v0.1.0-prototype 本地可用基线

- 用户确认当前功能已经可以使用，要求先冻结 0.1 版本并开始日常使用；后续继续开发到可发布安装包，再创建 GitHub 仓库开源。
- 冻结范围：全部源码、决策与过程记录、`0.1.0-prototype` 程序集版本、Changelog、冻结说明、打包脚本和回退方法。暂不创建或推送任何远程仓库。
- 隐私审计：构建/日志/缓存/artifacts 均已忽略；未发现蓝牙地址、密码、Token 或 API Key；过程日志中的本机用户名路径已替换为 `%USERPROFILE%`。
- Release 与按键探针构建均通过，0 个警告、0 个错误。
- 生成 Windows x64 framework-dependent 冻结目录与 ZIP；包内包含托盘、GATT 语音桥、.NET 依赖、发布说明和逐文件 `SHA256SUMS.txt`。
- 从 ZIP 解压副本完成 8 秒真实启动验证：逐文件 checksum 失败 0，产品版本 `0.1.0-prototype`、文件版本 `0.1.0.0`、托盘退出码 0、4 个清理标记齐全、残留 MiVibe 进程 0。
- ZIP SHA-256：`f58089b5dbb7ab7f4919eccbc8e7e118d41ac97c7575ff6a64f1fdf8c7466222`。旁置同名 `.sha256` 文件供后续校验。
- Git 冻结动作：本节随第一个本地基线提交保存，并创建注释标签 `v0.1.0-prototype`；下一开发阶段从该标签之后继续，不修改冻结 artifacts。

### 进入 0.2 开发线：自动恢复与日志保留

- 用户确认 0.1 已可日常使用，并要求保持本对话继续软件开发；LifeOS walkthrough/spec 改由 Claudian Codex 读取仓库记录后整理，避免在当前开发上下文重复承载过程材料。
- 版本从已冻结的 `0.1.0-prototype` 切换为 `0.2.0-alpha.1`；旧提交、标签、目录、ZIP 与 SHA-256 均未修改。
- 托盘增加意外退出自动恢复：2、5、10、30 秒递增并封顶；首次失败提示一次，成功恢复后清空重试计数。
- 暂停与安全退出会取消待执行重连；等待期菜单改为“暂停自动重连”，不需要用户抢在定时器前操作。
- resident 增加真实 BLE 断线出口：GATT session 不再 Active 时立即结束；8 秒 ATVV 保活写入失败时同样结束。旧会话随后沿既有 finally 路径关闭 MIC、取消订阅、释放 VB-CABLE 与 TV/反引号钩子。
- 托盘输出改为单一宿主生命周期日志；启动时保留最近 10 份，自动重连不再无限制造未受控日志。
- Debug 构建通过：0 个警告、0 个错误。
- 最终隐藏调度测试：托盘退出码 0，桥启动 3 次，观察到 2 秒和 5 秒退避，重连计划 3 次，开发构建残留进程 0。系统中仍运行的进程经核对是用户正在日常使用的冻结 0.1 包，不是测试残留，未被停止或修改。
- 下一验收：在用户准备切换时安全退出冻结 0.1，启动 0.2 开发构建，关闭遥控器或蓝牙后观察托盘进入自动重连，再恢复设备并验证 Typeless 与 Codex Voice。

### 长语音中断定位为遥控器单段约 60 秒边界

- 用户在日常使用冻结 0.1 时发现长语音会提前停止，期望与 Typeless 约 10 分钟的单次输入能力对齐。
- 对冻结版本真实托盘日志逐段计算：近期长段分别为 `59.999`、`59.999`、`60.000`、`59.954` 秒；并非随机的两三分钟，也不是 Typeless 先结束。
- 每个长段期间，宿主仍按 8 秒周期携带当前 stream ID 成功发送 `MIC_EXTEND`。因此现有证据不支持“续期间隔过长”或“常驻桥自身超时”的判断。
- 0.2 开发版加入只读诊断：每个实体麦克风段结束时记录精确持续时间、ATVV stop reason，以及系统映射后的 F13 是否仍处于按住状态。
- 显式 `--voice-capture`、`--voice-live`、`--typeless-live`、`--codex-voice-live`、`--tv-codex-voice-live` 测试窗口上限由 60 秒放宽为 900 秒；`--resident` 本来就没有固定上限，行为不变。
- 安全边界：本轮不自动重开麦克风。必须先验证固件在 60 秒停止帧到达时是否仍保留物理按住状态；只有能可靠确认“用户仍按住”时，才允许自动滚动到下一 ATVV 段，防止误把真正松手解释为继续录音。
- GATT 核心与托盘 Debug 构建通过，0 个警告、0 个错误；冻结 0.1 包及正在运行的 0.1 进程均未修改。

### 长语音自动续段改为延期优化

- 用户进一步确认：在 Typeless 约 10 分钟的同一次输入窗口内，可以多次按住和松开遥控器麦克风；说完一句后松手换气，再按住即可继续输入后续内容。
- 因此 60 秒约束只限制单个实体按住段，不会强制结束整个 Typeless 长内容输入任务。当前交互存在少量摩擦，但不影响实际使用。
- 产品决定：暂不进行超过 65 秒的按键状态诊断，也不实现自动 `MIC_OPEN` 滚动续段；该能力列入以后体验优化，不阻塞 0.2 稳定性、托盘完善和安装包发布。
- 已加入 0.2 的只读段结束诊断与 900 秒显式测试窗口保留，便于未来恢复研究；它们不会自动重开麦克风，不改变冻结 0.1。

### 0.2 继续运行，并纳入轻量前端与电量需求

- 用户确认可以继续开启 0.2，并希望蓝牙稳定后增加非常简单的前端，最好显示遥控器电量；该需求允许放到最后 phase，不能反向阻塞核心功能。
- 只读检查确认当前运行进程来自源码 Release 路径，文件版本 `0.2.0.0`、产品版本 `0.2.0-alpha.1`；resident 子进程处于活动状态并已收到多个真实麦克风段。
- 路线拆分：先完成实体蓝牙断开/自动恢复验收；随后实现 `0x180F / 0x2A19` 电量数据层；处理开机启动、日志与安装包基础；最后用现有 WinForms 托盘增加轻量状态窗口。
- 首版窗口只含设备、连接状态、电量、启动/暂停和音频体检；完整可视化遥控器与映射编辑器延期，不阻塞 0.2。

### 0.2 发布范围最终收敛为四项

- 用户明确 0.2 只需在 0.1 基础上加入：单实例托盘前端、蓝牙连接/重新连接按钮、电量提示、开机自动启动。
- 四项稳定运行后即可把该版本作为 0.2 推送到 GitHub；完整按键映射 UI、驱动、长语音自动续段和敏感宏均不进入本版本。
- 当前状态核对：托盘 MVP 已可用；自动退避重连已有代码和调度测试，但实体断开/恢复仍待验收；主动重连文案与安全重启行为需补齐；电量只确认服务存在；开机启动代码已编译但测试因用户中断尚未完成。
- 新增 `docs/RELEASE_V0.2_SCOPE.md`，将功能边界、非目标、发布验收和当前进度固定为后续开发基准。
