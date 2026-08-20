using System.Diagnostics;
using System.Drawing;
using System.Text;

namespace MiVibe.Remote.Tray;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly Control dispatcher = new();
    private readonly NotifyIcon notifyIcon;
    private readonly ToolStripMenuItem statusItem;
    private readonly ToolStripMenuItem startItem;
    private readonly ToolStripMenuItem pauseItem;
    private readonly ToolStripMenuItem audioStatusItem;
    private readonly ToolStripMenuItem exitItem;
    private readonly object logLock = new();
    private readonly System.Windows.Forms.Timer? smokeTestTimer;

    private Process? bridgeProcess;
    private EventWaitHandle? shutdownEvent;
    private StreamWriter? logWriter;
    private bool stopping;
    private bool exiting;

    public TrayApplicationContext(int? smokeTestSeconds)
    {
        dispatcher.CreateControl();

        statusItem = new ToolStripMenuItem("状态：正在启动") { Enabled = false };
        startItem = new ToolStripMenuItem("启动语音桥", null, OnStartClicked);
        pauseItem = new ToolStripMenuItem("暂停语音桥", null, OnPauseClicked);
        audioStatusItem = new ToolStripMenuItem("检查音频路由", null, OnAudioStatusClicked);
        exitItem = new ToolStripMenuItem("安全退出", null, OnExitClicked);

        var menu = new ContextMenuStrip();
        menu.Items.Add(statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(startItem);
        menu.Items.Add(pauseItem);
        menu.Items.Add(audioStatusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);

        notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Information,
            Text = "MiVibe Remote：正在启动",
            ContextMenuStrip = menu,
            Visible = true
        };
        notifyIcon.DoubleClick += OnAudioStatusClicked;

        SetStatus("正在启动", bridgeIsRunning: false);
        _ = StartBridgeAsync(showBalloon: false);

        if (smokeTestSeconds is not null)
        {
            smokeTestTimer = new System.Windows.Forms.Timer
            {
                Interval = checked(smokeTestSeconds.Value * 1000)
            };
            smokeTestTimer.Tick += OnSmokeTestElapsed;
            smokeTestTimer.Start();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            notifyIcon.Visible = false;
            notifyIcon.Dispose();
            smokeTestTimer?.Dispose();
            dispatcher.Dispose();
            shutdownEvent?.Dispose();
            lock (logLock)
            {
                logWriter?.Dispose();
                logWriter = null;
            }
        }

        base.Dispose(disposing);
    }

    private async void OnStartClicked(object? sender, EventArgs args)
    {
        await StartBridgeAsync(showBalloon: true);
    }

    private async void OnPauseClicked(object? sender, EventArgs args)
    {
        await StopBridgeAsync("已暂停");
    }

    private async void OnAudioStatusClicked(object? sender, EventArgs args)
    {
        audioStatusItem.Enabled = false;
        try
        {
            string result = await RunProbeAsync("--audio-status");
            MessageBox.Show(
                result,
                "MiVibe Remote · 音频路由",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception exception)
        {
            ShowError("无法检查音频路由", exception);
        }
        finally
        {
            audioStatusItem.Enabled = true;
        }
    }

    private async void OnExitClicked(object? sender, EventArgs args)
    {
        if (exiting)
        {
            return;
        }

        exiting = true;
        exitItem.Enabled = false;
        await StopBridgeAsync("已停止");
        notifyIcon.Visible = false;
        ExitThread();
    }

    private void OnSmokeTestElapsed(object? sender, EventArgs args)
    {
        smokeTestTimer?.Stop();
        OnExitClicked(sender, args);
    }

    private Task StartBridgeAsync(bool showBalloon)
    {
        if (bridgeProcess is { HasExited: false })
        {
            return Task.CompletedTask;
        }

        stopping = false;
        SetStatus("正在连接遥控器", bridgeIsRunning: false);

        try
        {
            string eventName = $"Local\\MiVibeRemote-{Environment.ProcessId}-{Guid.NewGuid():N}";
            shutdownEvent?.Dispose();
            shutdownEvent = new EventWaitHandle(
                initialState: false,
                EventResetMode.AutoReset,
                eventName);

            OpenLog();
            ProcessStartInfo startInfo = CreateProbeStartInfo(
                $"--resident --shutdown-event \"{eventName}\"");
            var process = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true
            };
            process.OutputDataReceived += OnBridgeOutput;
            process.ErrorDataReceived += OnBridgeOutput;
            process.Exited += OnBridgeExited;

            if (!process.Start())
            {
                throw new InvalidOperationException("后台语音桥进程没有启动。");
            }

            bridgeProcess = process;
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            SetStatus("正在初始化音频桥", bridgeIsRunning: true);

            if (showBalloon)
            {
                ShowBalloon("MiVibe Remote", "正在连接小米遥控器…");
            }
        }
        catch (Exception exception)
        {
            SetStatus("启动失败", bridgeIsRunning: false);
            ShowError("无法启动语音桥", exception);
            CleanupExitedProcess();
        }

        return Task.CompletedTask;
    }

    private async Task StopBridgeAsync(string completedStatus)
    {
        Process? process = bridgeProcess;
        if (process is null || process.HasExited)
        {
            CleanupExitedProcess();
            SetStatus(completedStatus, bridgeIsRunning: false);
            return;
        }

        stopping = true;
        SetStatus("正在安全停止", bridgeIsRunning: true);
        shutdownEvent?.Set();

        bool exited = await Task.Run(() => process.WaitForExit(10_000));
        if (!exited)
        {
            SetStatus("停止超时，请重试", bridgeIsRunning: true);
            ShowBalloon(
                "MiVibe Remote",
                "语音桥仍在清理蓝牙连接，没有强制终止。请稍后再次点击安全退出。");
            stopping = false;
            return;
        }

        CleanupExitedProcess();
        SetStatus(completedStatus, bridgeIsRunning: false);
        if (!exiting)
        {
            ShowBalloon("MiVibe Remote", "语音桥已暂停，遥控器按键钩子已恢复。");
        }
    }

    private void OnBridgeOutput(object sender, DataReceivedEventArgs args)
    {
        if (args.Data is null)
        {
            return;
        }

        lock (logLock)
        {
            logWriter?.WriteLine(args.Data);
        }

        if (args.Data.Contains("Resident bridge armed", StringComparison.Ordinal))
        {
            PostToUi(() =>
            {
                SetStatus("运行中", bridgeIsRunning: true);
                ShowBalloon("MiVibe Remote", "语音桥已就绪：开关键用于 Typeless，TV 键用于 Codex Voice。");
            });
        }
        else if (args.Data.Contains("ATVV capture failed", StringComparison.OrdinalIgnoreCase) ||
                 args.Data.Contains("was not found", StringComparison.OrdinalIgnoreCase))
        {
            PostToUi(() => SetStatus("连接失败，请检查日志", bridgeIsRunning: true));
        }
    }

    private void OnBridgeExited(object? sender, EventArgs args)
    {
        PostToUi(() =>
        {
            if (stopping || exiting)
            {
                return;
            }

            CleanupExitedProcess();
            SetStatus("意外停止", bridgeIsRunning: false);
            ShowBalloon("MiVibe Remote", "语音桥已停止。可从托盘菜单重新启动。");
        });
    }

    private async Task<string> RunProbeAsync(string arguments)
    {
        using var process = new Process
        {
            StartInfo = CreateProbeStartInfo(arguments)
        };
        if (!process.Start())
        {
            throw new InvalidOperationException("诊断进程没有启动。");
        }

        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        string combined = (await output) + (await error);
        return string.IsNullOrWhiteSpace(combined) ? "没有返回音频设备信息。" : combined.Trim();
    }

    private static ProcessStartInfo CreateProbeStartInfo(string arguments)
    {
        string probePath = Path.Combine(
            AppContext.BaseDirectory,
            "MiVibe.Remote.GattProbe.dll");
        if (!File.Exists(probePath))
        {
            throw new FileNotFoundException("找不到语音桥组件。", probePath);
        }

        return new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"\"{probePath}\" {arguments}",
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
    }

    private void OpenLog()
    {
        lock (logLock)
        {
            logWriter?.Dispose();
            string logDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(logDirectory);
            string logPath = Path.Combine(
                logDirectory,
                $"tray-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.log");
            logWriter = new StreamWriter(logPath, append: false, Encoding.UTF8)
            {
                AutoFlush = true
            };
        }
    }

    private void CleanupExitedProcess()
    {
        Process? process = bridgeProcess;
        bridgeProcess = null;
        if (process is not null)
        {
            process.OutputDataReceived -= OnBridgeOutput;
            process.ErrorDataReceived -= OnBridgeOutput;
            process.Exited -= OnBridgeExited;
            process.Dispose();
        }

        shutdownEvent?.Dispose();
        shutdownEvent = null;
        lock (logLock)
        {
            logWriter?.Dispose();
            logWriter = null;
        }
    }

    private void SetStatus(string status, bool bridgeIsRunning)
    {
        statusItem.Text = $"状态：{status}";
        notifyIcon.Text = $"MiVibe Remote：{status}";
        startItem.Enabled = !bridgeIsRunning && !exiting;
        pauseItem.Enabled = bridgeIsRunning && !exiting;
    }

    private void ShowBalloon(string title, string message)
    {
        notifyIcon.BalloonTipTitle = title;
        notifyIcon.BalloonTipText = message;
        notifyIcon.ShowBalloonTip(3000);
    }

    private static void ShowError(string title, Exception exception)
    {
        MessageBox.Show(
            exception.Message,
            title,
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    private void PostToUi(Action action)
    {
        if (dispatcher.IsDisposed)
        {
            return;
        }

        dispatcher.BeginInvoke(action);
    }
}
