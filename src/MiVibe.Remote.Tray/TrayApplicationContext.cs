using System.Diagnostics;
using System.Drawing;
using System.Text;

namespace MiVibe.Remote.Tray;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private static readonly int[] ReconnectDelaysSeconds = [2, 5, 10, 30];
    private const int MaximumRetainedLogFiles = 10;
    private const string RemoteDeviceName = "小米蓝牙语音遥控器";

    private readonly Control dispatcher = new();
    private readonly NotifyIcon notifyIcon;
    private readonly ToolStripMenuItem statusItem;
    private readonly ToolStripMenuItem batteryItem;
    private readonly ToolStripMenuItem startItem;
    private readonly ToolStripMenuItem pauseItem;
    private readonly ToolStripMenuItem audioStatusItem;
    private readonly ToolStripMenuItem startWithWindowsItem;
    private readonly ToolStripMenuItem exitItem;
    private readonly object logLock = new();
    private readonly System.Windows.Forms.Timer? smokeTestTimer;
    private readonly System.Windows.Forms.Timer reconnectTimer;
    private readonly bool reconnectSmokeTest;

    private Process? bridgeProcess;
    private EventWaitHandle? shutdownEvent;
    private StreamWriter? logWriter;
    private bool stopping;
    private bool exiting;
    private bool paused;
    private bool reconnectScheduled;
    private int reconnectAttempt;
    private StatusWindow? statusWindow;
    private ConnectionPhase connectionPhase = ConnectionPhase.Starting;
    private string connectionDetail = "正在启动";
    private int? reconnectDelaySeconds;
    private int? batteryLevel;
    private BatteryFreshness batteryFreshness = BatteryFreshness.Unknown;
    private DateTimeOffset? batteryUpdatedAt;

    public TrayApplicationContext(
        int? smokeTestSeconds,
        bool reconnectSmokeTest,
        bool showStatusWindow)
    {
        this.reconnectSmokeTest = reconnectSmokeTest;
        dispatcher.CreateControl();
        reconnectTimer = new System.Windows.Forms.Timer();
        reconnectTimer.Tick += OnReconnectTimerElapsed;
        TryOpenLog();

        statusItem = new ToolStripMenuItem("状态：正在启动") { Enabled = false };
        batteryItem = new ToolStripMenuItem("电量：未知") { Enabled = false };
        startItem = new ToolStripMenuItem("连接遥控器", null, OnStartClicked);
        pauseItem = new ToolStripMenuItem("暂停语音桥", null, OnPauseClicked);
        audioStatusItem = new ToolStripMenuItem("检查音频路由", null, OnAudioStatusClicked);
        startWithWindowsItem = new ToolStripMenuItem(
            "开机自动启动",
            null,
            OnStartWithWindowsClicked)
        {
            CheckOnClick = false,
            Checked = StartupRegistration.IsEnabledForCurrentExecutable()
        };
        exitItem = new ToolStripMenuItem("安全退出", null, OnExitClicked);

        var menu = new ContextMenuStrip();
        menu.Items.Add(statusItem);
        menu.Items.Add(batteryItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(startItem);
        menu.Items.Add(pauseItem);
        menu.Items.Add(audioStatusItem);
        menu.Items.Add(startWithWindowsItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);

        notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Information,
            Text = "MiVibe Remote：正在启动",
            ContextMenuStrip = menu,
            Visible = true
        };
        notifyIcon.DoubleClick += OnNotifyIconDoubleClick;

        SetStatus(
            ConnectionPhase.Starting,
            "正在启动",
            bridgeIsRunning: false);
        _ = StartBridgeAsync(showBalloon: false);
        if (showStatusWindow)
        {
            OnNotifyIconDoubleClick(this, EventArgs.Empty);
        }

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
            statusWindow?.AllowCloseAndDispose();
            statusWindow = null;
            smokeTestTimer?.Dispose();
            reconnectTimer.Dispose();
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

    private void OnNotifyIconDoubleClick(object? sender, EventArgs args)
    {
        StatusWindow window = GetOrCreateStatusWindow();
        window.ApplyState(CreateUiState());
        window.ShowOrActivate();
    }

    private StatusWindow GetOrCreateStatusWindow()
    {
        if (statusWindow is not null && !statusWindow.IsDisposed)
        {
            return statusWindow;
        }

        var window = new StatusWindow();
        window.ConnectOrReconnectRequested += OnStartClicked;
        window.PauseRequested += OnPauseClicked;
        window.AudioRouteRequested += OnAudioStatusClicked;
        window.StartWithWindowsToggleRequested += OnStartWithWindowsClicked;
        window.SafeExitRequested += OnExitClicked;
        statusWindow = window;
        return window;
    }

    private async void OnStartClicked(object? sender, EventArgs args)
    {
        if (stopping || exiting)
        {
            return;
        }

        startItem.Enabled = false;
        bool hasActiveBridge = bridgeProcess is { HasExited: false };
        if (hasActiveBridge)
        {
            bool stopped = await TryStopBridgeAsync(
                "已断开",
                markPaused: false,
                showBalloon: false);
            if (!stopped)
            {
                return;
            }
        }

        paused = false;
        reconnectAttempt = 0;
        CancelReconnect();
        await StartBridgeAsync(showBalloon: true);
    }

    private async void OnPauseClicked(object? sender, EventArgs args)
    {
        if (stopping || exiting)
        {
            return;
        }

        await TryStopBridgeAsync(
            "已暂停",
            markPaused: true,
            showBalloon: true);
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

    private void OnStartWithWindowsClicked(object? sender, EventArgs args)
    {
        try
        {
            bool enable = !StartupRegistration.IsEnabledForCurrentExecutable();
            StartupRegistration.SetEnabled(enable);
            startWithWindowsItem.Checked = enable;
            PublishUiState();
            WriteLog($"Start with Windows changed: enabled={enable}.");
            ShowBalloon(
                "MiVibe Remote",
                enable
                    ? "已启用：下次登录 Windows 后自动启动。"
                    : "已关闭开机自动启动。");
        }
        catch (Exception exception)
        {
            startWithWindowsItem.Checked =
                StartupRegistration.IsEnabledForCurrentExecutable();
            PublishUiState();
            ShowError("无法修改开机自动启动", exception);
        }
    }

    private async void OnExitClicked(object? sender, EventArgs args)
    {
        if (exiting || stopping)
        {
            return;
        }

        exiting = true;
        paused = true;
        CancelReconnect();
        exitItem.Enabled = false;
        bool stopped = await TryStopBridgeAsync(
            "已停止",
            markPaused: true,
            showBalloon: false);
        if (!stopped)
        {
            paused = true;
            exiting = false;
            exitItem.Enabled = true;
            return;
        }

        notifyIcon.Visible = false;
        ExitThread();
    }

    private void OnSmokeTestElapsed(object? sender, EventArgs args)
    {
        smokeTestTimer?.Stop();
        OnExitClicked(sender, args);
    }

    private void OnReconnectTimerElapsed(object? sender, EventArgs args)
    {
        reconnectTimer.Stop();
        reconnectScheduled = false;
        WriteLog($"Automatic reconnect attempt {reconnectAttempt} starting.");
        _ = StartBridgeAsync(showBalloon: false);
    }

    private Task StartBridgeAsync(bool showBalloon)
    {
        if (bridgeProcess is { HasExited: false })
        {
            return Task.CompletedTask;
        }

        if (bridgeProcess is not null)
        {
            CleanupExitedProcess();
        }

        stopping = false;
        paused = false;
        CancelReconnect();
        SetStatus(
            ConnectionPhase.Connecting,
            "正在连接遥控器",
            bridgeIsRunning: false);

        try
        {
            string eventName = $"Local\\MiVibeRemote-{Environment.ProcessId}-{Guid.NewGuid():N}";
            shutdownEvent?.Dispose();
            shutdownEvent = new EventWaitHandle(
                initialState: false,
                EventResetMode.AutoReset,
                eventName);

            ProcessStartInfo startInfo = CreateProbeStartInfo(
                reconnectSmokeTest
                    ? "--audio-status"
                    : $"--resident --shutdown-event \"{eventName}\"");
            var process = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true
            };
            process.OutputDataReceived += OnBridgeOutput;
            process.ErrorDataReceived += OnBridgeOutput;
            process.Exited += OnBridgeExited;
            bridgeProcess = process;

            if (!process.Start())
            {
                throw new InvalidOperationException("后台语音桥进程没有启动。");
            }

            WriteLog($"Bridge process started: pid={process.Id} automaticAttempt={reconnectAttempt}.");
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            SetStatus(
                ConnectionPhase.Connecting,
                "正在初始化音频桥",
                bridgeIsRunning: true);

            if (showBalloon)
            {
                ShowBalloon("MiVibe Remote", "正在连接小米遥控器…");
            }
        }
        catch (Exception exception)
        {
            WriteLog($"Bridge startup failed: {exception.GetType().Name}: {exception.Message}");
            CleanupExitedProcess();
            SetStatus(
                ConnectionPhase.Error,
                "启动失败",
                bridgeIsRunning: false);
            ScheduleReconnect();
        }

        return Task.CompletedTask;
    }

    private async Task<bool> TryStopBridgeAsync(
        string completedStatus,
        bool markPaused,
        bool showBalloon)
    {
        paused = markPaused;
        CancelReconnect();
        Process? process = bridgeProcess;
        if (process is null || process.HasExited)
        {
            CleanupExitedProcess();
            stopping = false;
            SetStatus(
                markPaused ? ConnectionPhase.Paused : ConnectionPhase.Disconnected,
                completedStatus,
                bridgeIsRunning: false);
            return true;
        }

        stopping = true;
        SetStatus(
            ConnectionPhase.Stopping,
            "正在安全停止",
            bridgeIsRunning: true);
        shutdownEvent?.Set();

        bool exited = await Task.Run(() =>
        {
            bool completed = process.WaitForExit(10_000);
            if (completed)
            {
                process.WaitForExit();
            }

            return completed;
        });
        if (!exited)
        {
            stopping = false;
            SetStatus(
                ConnectionPhase.Error,
                "停止超时，请重试",
                bridgeIsRunning: true);
            ShowBalloon(
                "MiVibe Remote",
                "语音桥仍在清理蓝牙连接，没有强制终止。请稍后再次点击安全退出。");
            return false;
        }

        CleanupExitedProcess();
        stopping = false;
        SetStatus(
            markPaused ? ConnectionPhase.Paused : ConnectionPhase.Disconnected,
            completedStatus,
            bridgeIsRunning: false);
        if (!exiting && showBalloon)
        {
            ShowBalloon("MiVibe Remote", "语音桥已暂停，遥控器按键钩子已恢复。");
        }

        return true;
    }

    private void OnBridgeOutput(object sender, DataReceivedEventArgs args)
    {
        if (args.Data is null ||
            sender is not Process sourceProcess ||
            !ReferenceEquals(bridgeProcess, sourceProcess))
        {
            return;
        }

        WriteLog($"[bridge] {args.Data}");

        if (BatteryBridgeProtocol.TryParse(args.Data, out BatteryBridgeEvent batteryEvent))
        {
            PostBridgeToUi(sourceProcess, () => ApplyBatteryEvent(batteryEvent));
        }
        else if (args.Data.Contains("Resident bridge armed", StringComparison.Ordinal))
        {
            PostBridgeToUi(sourceProcess, () =>
            {
                bool recovered = reconnectAttempt > 0;
                reconnectAttempt = 0;
                SetStatus(
                    ConnectionPhase.Connected,
                    "运行中",
                    bridgeIsRunning: true);
                ShowBalloon(
                    "MiVibe Remote",
                    recovered
                        ? "蓝牙语音桥已自动恢复。"
                        : "语音桥已就绪：开关键用于 Typeless，菜单键用于 Codex Voice，Home 键用于 Delete。");
            });
        }
        else if (args.Data.Contains("ATVV capture failed", StringComparison.OrdinalIgnoreCase) ||
                 args.Data.Contains("was not found", StringComparison.OrdinalIgnoreCase))
        {
            PostBridgeToUi(
                sourceProcess,
                () => SetStatus(
                    ConnectionPhase.Error,
                    "连接失败，请检查日志",
                    bridgeIsRunning: true));
        }
    }

    private void OnBridgeExited(object? sender, EventArgs args)
    {
        var exitedProcess = sender as Process;
        PostToUi(() =>
        {
            if (!ReferenceEquals(bridgeProcess, exitedProcess))
            {
                return;
            }

            if (stopping || exiting)
            {
                return;
            }

            int? exitCode = exitedProcess is { HasExited: true }
                ? exitedProcess.ExitCode
                : null;
            WriteLog($"Bridge process exited unexpectedly: exitCode={exitCode?.ToString() ?? "unknown"}.");
            CleanupExitedProcess();
            ScheduleReconnect();
        });
    }

    private void ScheduleReconnect()
    {
        if (paused || exiting || stopping || reconnectScheduled)
        {
            return;
        }

        int delayIndex = Math.Min(reconnectAttempt, ReconnectDelaysSeconds.Length - 1);
        int delaySeconds = ReconnectDelaysSeconds[delayIndex];
        reconnectAttempt++;
        reconnectScheduled = true;
        reconnectDelaySeconds = delaySeconds;
        reconnectTimer.Interval = delaySeconds * 1000;
        reconnectTimer.Start();
        SetStatus(
            ConnectionPhase.ReconnectWaiting,
            $"等待自动重连（{delaySeconds} 秒）",
            bridgeIsRunning: true);
        WriteLog($"Automatic reconnect scheduled: attempt={reconnectAttempt} delaySeconds={delaySeconds}.");

        if (reconnectAttempt == 1)
        {
            ShowBalloon("MiVibe Remote", $"语音桥已断开，将在 {delaySeconds} 秒后自动重连。");
        }
    }

    private void CancelReconnect()
    {
        reconnectTimer.Stop();
        reconnectScheduled = false;
        reconnectDelaySeconds = null;
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

    private void TryOpenLog()
    {
        try
        {
            OpenLog();
        }
        catch (IOException)
        {
            // A read-only install location must not prevent the remote from starting.
        }
        catch (UnauthorizedAccessException)
        {
            // A future installer will move logs to a per-user data directory.
        }
    }

    private void OpenLog()
    {
        lock (logLock)
        {
            logWriter?.Dispose();
            string logDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(logDirectory);
            PruneLogs(logDirectory);
            string logPath = Path.Combine(
                logDirectory,
                $"tray-{DateTimeOffset.Now:yyyyMMdd-HHmmss}-{Environment.ProcessId}.log");
            logWriter = new StreamWriter(logPath, append: false, Encoding.UTF8)
            {
                AutoFlush = true
            };
            logWriter.WriteLine($"{DateTimeOffset.Now:O} Tray host started.");
        }
    }

    private static void PruneLogs(string logDirectory)
    {
        FileInfo[] oldLogs = new DirectoryInfo(logDirectory)
            .GetFiles("tray-*.log")
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Skip(MaximumRetainedLogFiles - 1)
            .ToArray();

        foreach (FileInfo oldLog in oldLogs)
        {
            try
            {
                oldLog.Delete();
            }
            catch (IOException)
            {
                // A log may still be open from another tray instance. Keep it and continue.
            }
            catch (UnauthorizedAccessException)
            {
                // Logging must never prevent the remote from starting.
            }
        }
    }

    private void WriteLog(string message)
    {
        lock (logLock)
        {
            logWriter?.WriteLine($"{DateTimeOffset.Now:O} {message}");
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
        MarkBatteryAsLastKnown();
    }

    private void SetStatus(
        ConnectionPhase phase,
        string status,
        bool bridgeIsRunning)
    {
        connectionPhase = phase;
        connectionDetail = status;
        statusItem.Text = $"状态：{status}";
        notifyIcon.Text = $"MiVibe Remote：{status}";
        startItem.Enabled = !stopping &&
            !exiting &&
            phase is not ConnectionPhase.Starting and
                not ConnectionPhase.Connecting and
                not ConnectionPhase.Stopping;
        startItem.Text = bridgeIsRunning || reconnectScheduled
            ? "重新连接遥控器"
            : "连接遥控器";
        pauseItem.Enabled = bridgeIsRunning &&
            !stopping &&
            !exiting &&
            phase is not ConnectionPhase.Connecting;
        pauseItem.Text = reconnectScheduled ? "暂停自动重连" : "暂停语音桥";
        exitItem.Enabled = !stopping && !exiting;
        PublishUiState();
    }

    private void ApplyBatteryEvent(BatteryBridgeEvent batteryEvent)
    {
        switch (batteryEvent.Kind)
        {
            case BatteryBridgeEventKind.Level when batteryEvent.Level is int level:
                batteryLevel = level;
                batteryFreshness = BatteryFreshness.Current;
                batteryUpdatedAt = DateTimeOffset.Now;
                break;

            case BatteryBridgeEventKind.Unknown:
                batteryLevel = null;
                batteryFreshness = BatteryFreshness.Unknown;
                batteryUpdatedAt = null;
                break;

            case BatteryBridgeEventKind.Stale:
                MarkBatteryAsLastKnown();
                break;
        }

        PublishUiState();
    }

    private void MarkBatteryAsLastKnown()
    {
        batteryFreshness = batteryLevel is null
            ? BatteryFreshness.Unknown
            : BatteryFreshness.LastKnown;
    }

    private TrayUiState CreateUiState()
    {
        return new TrayUiState(
            DeviceName: RemoteDeviceName,
            Connection: connectionPhase,
            Detail: connectionDetail,
            ReconnectDelaySeconds: reconnectDelaySeconds,
            BatteryPercent: batteryLevel,
            BatteryFreshness: batteryFreshness,
            BatteryUpdatedAt: batteryUpdatedAt,
            StartWithWindows: startWithWindowsItem.Checked,
            OperationInProgress: stopping ||
                connectionPhase is ConnectionPhase.Starting or
                    ConnectionPhase.Connecting or
                    ConnectionPhase.Stopping);
    }

    private void PublishUiState()
    {
        batteryItem.Text = batteryFreshness switch
        {
            BatteryFreshness.Current when batteryLevel is int level =>
                $"电量：{level}%",
            BatteryFreshness.LastKnown when batteryLevel is int level =>
                $"电量：{level}%（上次读取）",
            _ => "电量：未知"
        };

        statusWindow?.ApplyState(CreateUiState());
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

    private void PostBridgeToUi(Process sourceProcess, Action action)
    {
        PostToUi(() =>
        {
            if (ReferenceEquals(bridgeProcess, sourceProcess))
            {
                action();
            }
        });
    }
}
