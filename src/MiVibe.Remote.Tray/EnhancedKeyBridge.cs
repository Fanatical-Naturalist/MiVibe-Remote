using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace MiVibe.Remote.Tray;

/// <summary>Owns one elevated, observation-only helper through an authenticated local pipe.</summary>
internal sealed class EnhancedKeyBridge : IDisposable
{
    private readonly SemaphoreSlim lifecycle = new(1, 1);
    private readonly object stateGate = new();
    private Session? current;
    private bool disposed;

    public event Action<KeyBridgePhase, string, int>? StateChanged;
    public event Action<string>? KeyPressed;
    public event Action<string>? Diagnostic;

    public bool IsRunning
    {
        get
        {
            lock (stateGate)
            {
                return current is not null && HasLiveOrStartingProcess(current);
            }
        }
    }

    public async Task StartAsync()
    {
        await lifecycle.WaitAsync();
        Session? session = null;
        try
        {
            lock (stateGate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (current is not null && HasLiveOrStartingProcess(current))
                {
                    if (current.Faulted || current.Stopping)
                    {
                        Publish(KeyBridgePhase.Error, "增强组件仍在退出，请先关闭增强按键后重试。");
                    }
                    return;
                }
            }

            if (current is not null)
            {
                await ReleaseEndedSessionAsync(current);
            }

            string executable = Path.Combine(AppContext.BaseDirectory, "KeyBridge", "MiVibe.Remote.KeyBridge.exe");
            if (!File.Exists(executable))
            {
                Publish(KeyBridgePhase.Error, "缺少增强按键组件，请使用完整的 0.3 安装包。");
                return;
            }

            string pipeName = $"MiVibeRemote.Keys.{Guid.NewGuid():N}";
            session = new Session(pipeName);
            lock (stateGate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                current = session;
            }
            PublishFor(session, KeyBridgePhase.Starting, "请在 Windows 提示中允许启用增强按键。");
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = $"--pipe {pipeName} --parent-pid {Environment.ProcessId}",
                WorkingDirectory = Path.GetDirectoryName(executable)!,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };
            Session launchedSession = session;
            lock (stateGate)
            {
                if (disposed || session.Stopping)
                {
                    throw new OperationCanceledException(session.Cancellation.Token);
                }

                // Keep the task even if Stop cancels our await while UAC is open.
                // A late process still belongs to this session and blocks a duplicate launch.
                session.LaunchTask = Task.Run(() => Process.Start(startInfo)
                    ?? throw new InvalidOperationException("增强按键组件没有启动。"));
                _ = session.LaunchTask.ContinueWith(task => _ = task.Exception,
                    CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            }

            Process helper = await session.LaunchTask.WaitAsync(session.Cancellation.Token);
            using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(session.Cancellation.Token);
            connectTimeout.CancelAfter(TimeSpan.FromSeconds(30));
            await session.Pipe.WaitForConnectionAsync(connectTimeout.Token);
            if (!GetNamedPipeClientProcessId(session.Pipe.SafePipeHandle, out uint clientPid) ||
                clientPid != helper.Id || helper.HasExited)
            {
                throw new IOException("Enhanced helper client identity mismatch.");
            }

            lock (stateGate)
            {
                if (!ReferenceEquals(current, session) || disposed || session.Stopping ||
                    session.Cancellation.IsCancellationRequested)
                {
                    throw new OperationCanceledException(session.Cancellation.Token);
                }
                session.Writer = new StreamWriter(session.Pipe, new UTF8Encoding(false), 1024, leaveOpen: true)
                {
                    AutoFlush = true,
                    NewLine = "\n"
                };
                session.LastMessageTick = Environment.TickCount64;
                // Both loops operate only on captured session resources.
                session.ReadTask = Task.Run(() => ReadMessagesAsync(launchedSession));
                session.MonitorTask = Task.Run(() => MonitorAsync(launchedSession, helper));
            }
            Log("Enhanced key helper connected and client process authenticated.");
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            if (session is not null)
            {
                CloseTransport(session);
                await ReleaseEndedSessionAsync(session);
            }
            Publish(KeyBridgePhase.Disabled, "未启用增强按键；可随时再次点击启用。");
        }
        catch (OperationCanceledException) when (session is not null &&
            (session.Stopping || disposed))
        {
            CloseTransport(session);
        }
        catch (Exception exception)
        {
            Log($"Enhanced key startup failed: {exception.GetType().Name}.");
            if (session is not null)
            {
                Fail(session, "增强按键启动失败，请关闭增强按键后重试。语音功能可以继续使用。");
            }
            else
            {
                Publish(KeyBridgePhase.Error, "增强按键启动失败，请重试。");
            }
        }
        finally
        {
            lifecycle.Release();
        }
    }

    public async Task RecalibrateAsync()
    {
        await lifecycle.WaitAsync();
        try
        {
            Session? session;
            lock (stateGate)
            {
                session = current;
                if (disposed || session is null || session.Stopping || session.Faulted ||
                    session.Writer is null)
                {
                    return;
                }
                session.Active = false;
                session.CalibrationStep = -1;
                session.AwaitingCalibrationStart = true;
                Publish(KeyBridgePhase.Calibrating, "请依次按下并松开：返回 → 音量＋ → 音量－。");
            }

            try
            {
                await SendAsync(session, "recalibrate", session.Cancellation.Token);
            }
            catch (Exception exception)
            {
                Log($"Enhanced key recalibration failed: {exception.GetType().Name}.");
                Fail(session, "校准连接已断开，请关闭增强按键后重新启用。");
            }
        }
        finally
        {
            lifecycle.Release();
        }
    }

    public async Task<bool> StopAsync()
    {
        // Revoke key delivery immediately, including while Start awaits UAC/connection.
        Session? initial;
        lock (stateGate)
        {
            initial = current;
            if (initial is not null)
            {
                initial.Active = false;
                initial.Stopping = true;
                initial.Cancellation.Cancel();
            }
        }

        await lifecycle.WaitAsync();
        try
        {
            Session? session;
            lock (stateGate)
            {
                session = current;
                if (session is not null)
                {
                    session.Active = false;
                    session.Stopping = true;
                }
            }
            if (session is null)
            {
                Publish(KeyBridgePhase.Disabled, "增强按键已关闭。");
                return true;
            }

            Publish(KeyBridgePhase.Stopping, "正在恢复按键并关闭增强组件…");
            try
            {
                using var sendTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await SendAsync(session, "stop", sendTimeout.Token);
            }
            catch (Exception exception)
            {
                Log($"Enhanced key stop command ended: {exception.GetType().Name}.");
            }
            CloseTransport(session);

            try
            {
                using var exitTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
                if (session.LaunchTask is not null)
                {
                    Process? helper = null;
                    try
                    {
                        helper = await session.LaunchTask.WaitAsync(exitTimeout.Token);
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        Log($"Enhanced key launch ended without a process: {exception.GetType().Name}.");
                    }
                    if (helper is not null)
                    {
                        await helper.WaitForExitAsync(exitTimeout.Token);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Publish(KeyBridgePhase.Error, "增强组件仍在安全退出，请稍后重试。没有强制终止系统驱动。");
                return false; // Retain the launch task/process so retry can wait for it.
            }
            catch (Exception exception)
            {
                Log($"Enhanced key process wait failed: {exception.GetType().Name}.");
                if (HasLiveOrStartingProcess(session))
                {
                    Publish(KeyBridgePhase.Error, "尚未确认增强组件已退出，请稍后重试。");
                    return false;
                }
            }

            await ReleaseEndedSessionAsync(session);
            Publish(KeyBridgePhase.Disabled, "增强按键已关闭，原生按键行为已恢复。");
            return true;
        }
        finally
        {
            lifecycle.Release();
        }
    }

    private async Task ReadMessagesAsync(Session session)
    {
        try
        {
            byte[] buffer = new byte[1024];
            using var line = new MemoryStream(4096);
            var utf8 = new UTF8Encoding(false, true);
            while (!session.Cancellation.IsCancellationRequested)
            {
                int count = await session.Pipe.ReadAsync(buffer.AsMemory(), session.Cancellation.Token);
                if (count == 0)
                {
                    break;
                }

                for (int index = 0; index < count; index++)
                {
                    byte value = buffer[index];
                    if (value == (byte)'\n')
                    {
                        string text = utf8.GetString(line.GetBuffer(), 0, checked((int)line.Length));
                        HandleMessage(session, KeyBridgeMessage.Parse(text));
                        line.SetLength(0);
                    }
                    else
                    {
                        line.WriteByte(value);
                        if (line.Length >= 4096)
                        {
                            throw new IOException("Oversized key bridge message.");
                        }
                    }
                }
            }
        }
        catch (Exception exception)
        {
            Log($"Enhanced key connection ended: {exception.GetType().Name}.");
        }
        finally
        {
            Fail(session, "增强按键连接已断开，请关闭后重新启用。语音功能不受影响。");
        }
    }

    private void HandleMessage(Session session, KeyBridgeMessage message)
    {
        lock (stateGate)
        {
            if (!ReferenceEquals(current, session) || disposed || session.Stopping || session.Faulted)
            {
                return;
            }
            Interlocked.Exchange(ref session.LastMessageTick, Environment.TickCount64);
            if (message.Type == "heartbeat")
            {
                return;
            }

            if (message.Type == "key")
            {
                if (!session.Active || message.Sequence <= session.LastSequence)
                {
                    return;
                }
                session.LastSequence = message.Sequence;
                // Stop/Recalibrate cannot revoke permission between validation and dispatch.
                foreach (Action<string> handler in (KeyPressed?.GetInvocationList() ?? []).Cast<Action<string>>())
                {
                    try { handler(message.Key!); }
                    catch (Exception exception) { Log($"Enhanced key action observer failed: {exception.GetType().Name}."); }
                }
                return;
            }

            string phase = message.Phase!;
            if (phase == "calibrating")
            {
                session.Active = false;
                if (message.Step == 0)
                {
                    session.CalibrationStep = 0;
                    session.AwaitingCalibrationStart = false;
                }
                else if (!session.AwaitingCalibrationStart &&
                    (message.Step == session.CalibrationStep || message.Step == session.CalibrationStep + 1))
                {
                    session.CalibrationStep = message.Step;
                }
                else if (!session.AwaitingCalibrationStart)
                {
                    throw new IOException("Calibration status arrived out of order.");
                }
                Publish(KeyBridgePhase.Calibrating, "依次短按并松开：返回 → 音量＋ → 音量－。",
                    Math.Max(0, session.CalibrationStep));
                return;
            }

            if (phase == "active")
            {
                // A buffered active message cannot undo a local recalibration request.
                if (session.AwaitingCalibrationStart)
                {
                    return;
                }
                if (session.CalibrationStep != 2)
                {
                    throw new IOException("Active status without completed calibration.");
                }
                session.Active = true;
                Publish(KeyBridgePhase.Active, "返回键删除；音量键按活动列表上下切换任务。");
                return;
            }

            session.Active = false;
            session.CalibrationStep = -1;
            session.AwaitingCalibrationStart = true;
            (KeyBridgePhase state, string detail) = phase switch
            {
                "connecting" => (KeyBridgePhase.Starting, "正在连接遥控器按键…"),
                "reconnecting" => (KeyBridgePhase.Reconnecting, "正在重新连接按键，连接后需要重新校准。"),
                "stopped" => (KeyBridgePhase.Disabled, "增强按键已关闭。"),
                _ => (KeyBridgePhase.Error, "增强按键暂不可用，正在等待组件恢复。")
            };
            Publish(state, detail);
            if (phase == "stopped")
            {
                session.Stopping = true;
                CloseTransport(session);
            }
        }
    }

    private async Task MonitorAsync(Session session, Process helper)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
            while (await timer.WaitForNextTickAsync(session.Cancellation.Token))
            {
                if (helper.HasExited || Environment.TickCount64 - Interlocked.Read(ref session.LastMessageTick) > 15_000)
                {
                    Fail(session, "增强按键暂时失去响应，请关闭后重新启用。");
                    return;
                }
                await SendAsync(session, "ping", session.Cancellation.Token);
            }
        }
        catch (Exception exception)
        {
            Log($"Enhanced key monitor ended: {exception.GetType().Name}.");
            Fail(session, "增强按键连接已失去响应，请关闭后重新启用。");
        }
    }

    private static async Task SendAsync(Session session, string command, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        await session.Writes.WaitAsync(timeout.Token);
        try
        {
            StreamWriter target = session.Writer ?? throw new IOException("Helper pipe is not ready.");
            await target.WriteLineAsync(("{\"command\":\"" + command + "\"}").AsMemory(), timeout.Token);
            await target.FlushAsync(timeout.Token);
        }
        finally
        {
            session.Writes.Release();
        }
    }

    private void Fail(Session session, string detail)
    {
        lock (stateGate)
        {
            session.Active = false;
            session.CalibrationStep = -1;
            session.AwaitingCalibrationStart = true;
            if (ReferenceEquals(current, session) && !disposed && !session.Stopping && !session.Faulted)
            {
                session.Faulted = true;
                Publish(KeyBridgePhase.Error, detail);
            }
        }
        CloseTransport(session);
    }

    private static void CloseTransport(Session session)
    {
        session.Cancellation.Cancel();
        session.Pipe.Dispose();
    }

    private async Task ReleaseEndedSessionAsync(Session session)
    {
        CloseTransport(session);
        Task[] loops = new[] { session.ReadTask, session.MonitorTask }.OfType<Task>().ToArray();
        try { await Task.WhenAll(loops); }
        catch (Exception exception) { Log($"Enhanced key cleanup ended: {exception.GetType().Name}."); }
        if (HasLiveOrStartingProcess(session))
        {
            return;
        }

        lock (stateGate)
        {
            if (ReferenceEquals(current, session))
            {
                current = null;
            }
        }
        if (session.LaunchTask is { IsCompletedSuccessfully: true })
        {
            session.LaunchTask.Result.Dispose();
        }
        // Loops and command writes can race a fallback Dispose. The canceled CTS and
        // writer are kept with the closed session until GC, avoiding use-after-dispose.
    }

    private static bool HasLiveOrStartingProcess(Session session)
    {
        Task<Process>? launch = session.LaunchTask;
        if (launch is null)
        {
            return false;
        }
        if (!launch.IsCompleted)
        {
            return true;
        }
        if (!launch.IsCompletedSuccessfully)
        {
            return false;
        }
        try { return !launch.Result.HasExited; }
        catch (InvalidOperationException) { return false; }
        catch (Win32Exception) { return true; } // Cannot prove exit: retain ownership.
    }

    private void PublishFor(Session session, KeyBridgePhase state, string detail)
    {
        lock (stateGate)
        {
            if (ReferenceEquals(current, session) && !disposed && !session.Stopping)
            {
                Publish(state, detail);
            }
        }
    }

    private void Publish(KeyBridgePhase state, string detail, int step = 0)
    {
        foreach (Action<KeyBridgePhase, string, int> handler in
            (StateChanged?.GetInvocationList() ?? []).Cast<Action<KeyBridgePhase, string, int>>())
        {
            try { handler(state, detail, step); }
            catch (Exception exception) { Log($"Enhanced key state observer failed: {exception.GetType().Name}."); }
        }
    }

    private void Log(string text)
    {
        foreach (Action<string> handler in (Diagnostic?.GetInvocationList() ?? []).Cast<Action<string>>())
        {
            try { handler(text); }
            catch (Exception) { /* Diagnostics must never fault the transport tasks. */ }
        }
    }

    public void Dispose()
    {
        lock (stateGate)
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            if (current is not null)
            {
                current.Active = false;
                current.Stopping = true;
                CloseTransport(current);
            }
        }
        // UI shutdown awaits StopAsync. This fallback closes the owning pipe even
        // while a UAC launch or pipe connection is pending; no process is killed.
    }

    private sealed class Session
    {
        public Session(string pipeName) =>
            Pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, 4096, 4096);

        public readonly NamedPipeServerStream Pipe;
        public readonly CancellationTokenSource Cancellation = new();
        public readonly SemaphoreSlim Writes = new(1, 1);
        public Task<Process>? LaunchTask;
        public StreamWriter? Writer;
        public Task? ReadTask;
        public Task? MonitorTask;
        public long LastMessageTick;
        public long LastSequence;
        public int CalibrationStep = -1;
        public bool AwaitingCalibrationStart = true;
        public bool Active;
        public bool Stopping;
        public bool Faulted;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipeHandle, out uint clientProcessId);
}
