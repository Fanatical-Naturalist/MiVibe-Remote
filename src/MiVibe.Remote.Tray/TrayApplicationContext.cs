using System.Diagnostics;
using System.Drawing;
using System.Text;

namespace MiVibe.Remote.Tray;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private static readonly int[] ReconnectDelaysSeconds = [2, 5, 10, 30];
    private const int MaximumRetainedLogFiles = 10;

    private readonly Control dispatcher = new();
    private readonly NotifyIcon notifyIcon;
    private readonly ToolStripMenuItem statusItem;
    private readonly ToolStripMenuItem startItem;
    private readonly ToolStripMenuItem pauseItem;
    private readonly ToolStripMenuItem audioStatusItem;
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

    public TrayApplicationContext(int? smokeTestSeconds, bool reconnectSmokeTest)
    {
        this.reconnectSmokeTest = reconnectSmokeTest;
        dispatcher.CreateControl();
        reconnectTimer = new System.Windows.Forms.Timer();
        reconnectTimer.Tick += OnReconnectTimerElapsed;
        TryOpenLog();

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

    private async void OnStartClicked(object? sender, EventArgs args)
    {
        paused = false;
        reconnectAttempt = 0;
        CancelReconnect();
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
        paused = true;
        CancelReconnect();
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

        stopping = false;
        paused = false;
        CancelReconnect();
        SetStatus("正在连接遥控器", bridgeIsRunning: false);

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
            SetStatus("正在初始化音频桥", bridgeIsRunning: true);

            if (showBalloon)
            {
                ShowBalloon("MiVibe Remote", "正在连接小米遥控器…");
            }
        }
        catch (Exception exception)
        {
            SetStatus("启动失败", bridgeIsRunning: false);
            WriteLog($"Bridge startup failed: {exception.GetType().Name}: {exception.Message}");
            CleanupExitedProcess();
            ScheduleReconnect();
        }

        return Task.CompletedTask;
    }

    private async Task StopBridgeAsync(string completedStatus)
    {
        paused = !exiting;
        CancelReconnect();
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

        WriteLog($"[bridge] {args.Data}");

        if (args.Data.Contains("Resident bridge armed", StringComparison.Ordinal))
        {
            PostToUi(() =>
            {
                bool recovered = reconnectAttempt > 0;
                reconnectAttempt = 0;
                SetStatus("运行中", bridgeIsRunning: true);
                ShowBalloon(
                    "MiVibe Remote",
                    recovered
                        ? "蓝牙语音桥已自动恢复。"
                        : "语音桥已就绪：开关键用于 Typeless，TV 键用于 Codex Voice。");
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
        reconnectTimer.Interval = delaySeconds * 1000;
        reconnectTimer.Start();
        SetStatus($"等待自动重连（{delaySeconds} 秒）", bridgeIsRunning: true);
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
    }

    private void SetStatus(string status, bool bridgeIsRunning)
    {
        statusItem.Text = $"状态：{status}";
        notifyIcon.Text = $"MiVibe Remote：{status}";
        startItem.Enabled = !bridgeIsRunning && !exiting;
        pauseItem.Enabled = bridgeIsRunning && !exiting;
        pauseItem.Text = reconnectScheduled ? "暂停自动重连" : "暂停语音桥";
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
