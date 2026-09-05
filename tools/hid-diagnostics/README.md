# RC003 限时诊断

本目录是独立实验工具，不随当前安装版自动启动。实机结果及边界见
[诊断记录](../../docs/HID_DIAGNOSTIC_2026-09-05.md)。

## 准备

Windows x64、Python x64，项目内隔离依赖：

```powershell
python -m pip install --target .local/hid-probe-deps --only-binary=:all: --no-deps frida==17.15.3
```

以下 capture 命令需要在管理员终端执行。每次重新读取精确设备族对应 HostPid，
核实系统 WUDFHost 镜像，使用可取消的 Frida 操作和脚本内定时解除；
不自动重新附加其他进程，不终止 WUDFHost。

## 仅观察

```powershell
python tools/hid-diagnostics/capture.py --preflight
python tools/hid-diagnostics/capture.py --seconds 120
```

等日志出现 `ready` 再开始按键。默认不发送、不压制任何按键；
只读取指定 IOCTL 的九字节缓冲区，校验报告头和已知 usage，
只记录匿名报告流、已知键的边沿及诊断计数，不记录未知 usage 的具体值或音频。
同一个 WUDFHost 可共享多个设备，单靠 HostPid 不能认定数据来自目标设备。

## 可选动作测试

```powershell
python tools/hid-diagnostics/capture.py --seconds 180 --actions --navigation adjacent
```

1. 等待 `ready`，依次完整按下并松开返回、音量＋、音量－，各一次。
2. 等待 `action_armed`。校准结束前不发动作；只处理校准后的同一匿名流。
3. 返回发送一次扩展 Delete，仅允许 Codex 或记事本前台。
4. 音量＋/－仅在 Codex 前台发送 Ctrl＋PageUp/PageDown；先点击主对话区域，
   避免相同快捷键被聚焦的面板用来切换标签。
5. 有按住的修饰键、前台切换、过期/丢失事件时不发动作；断连或超时立即关闭发送器。

`--navigation none` 仅测试 Delete；`history` 测试鼠标侧键式后退/前进，
与用户当前选择的相邻对话导航不同。
每次启动需要重新校准，最长 180 秒；此行为不是已交付的自动重连常驻功能。

## 菜单 Translate 单独测试

```powershell
python tools/hid-diagnostics/menu_translate_test.py --seconds 90
```

该工具安装临时全局 Menu/Application 钩子，用户短按菜单时在后台发送
右 Shift＋T，保持约 80 ms 后完整释放，到期解除钩子。
电脑实体 Menu 也会在这段时间被占用。开始和结束时请松开菜单；
若原语音桥重连并安装了更新的钩子，需重新判断本次测试。
不会改变 Typeless 设置。`shortcut_submitted` 只证明 Windows 接受了输入，
功能成功必须由用户实际看到 Translate 界面确认。

## 清理与日志

正常结束包含 `script_unloaded`、主动 `detached`、`finished cleanupOk=true`；
菜单测试包含 `hook_removed success=true` 且 `pending_scans` 为空。
异常退出不能冒充清理成功，不用杀掉驱动宿主作为清理手段。
日志只落在忽略目录 `logs/hid-diagnostics`。
