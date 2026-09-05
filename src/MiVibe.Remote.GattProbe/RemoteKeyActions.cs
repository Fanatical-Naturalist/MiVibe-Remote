using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading.Channels;

namespace MiVibe.Remote.GattProbe;

/// <summary>Serializes trusted remote events and the native Menu/Home shortcuts.</summary>
public sealed class RemoteKeyActions : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const uint WmQuit = 0x0012;
    private const uint VkApps = 0x5D;
    private const uint VkHome = 0x24;
    private const uint LlkhfInjected = 0x00000010;

    private readonly ManualResetEventSlim ready = new(false);
    private readonly CancellationTokenSource actionCancellation = new();
    private readonly Channel<QueuedAction> translateActions = Channel.CreateBounded<QueuedAction>(
        new BoundedChannelOptions(8)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false
        });
    private readonly Task actionWorker;
    private readonly Thread thread;
    private readonly HookCallback callback;
    private IntPtr hook;
    private uint threadId;
    private Exception? startupError;
    private bool keyDown;
    private bool homeKeyDown;
    private int blockedEventCount;
    private int translateShortcutCount;
    private int skippedTranslateCount;
    private int droppedActionCount;
    private int deleteCount;
    private int navigationCount;
    private bool unhookSucceeded = true;
    private int unhookError;
    private int disposed;
    private int acceptingActions = 1;
    private bool threadStarted;

    /// <summary>Raised on a background thread after an action or a guarded skip.</summary>
    public event Action<string>? ActionObserved;

    /// <summary>False after disposal or failure of either the hook or action worker.</summary>
    public bool IsAvailable => Volatile.Read(ref disposed) == 0 &&
        Volatile.Read(ref acceptingActions) != 0 &&
        thread.IsAlive && !actionWorker.IsCompleted;

    public RemoteKeyActions()
    {
        callback = OnKeyboardEvent;
        thread = new Thread(RunMessageLoop)
        {
            IsBackground = true,
            Name = "MiVibe remote-key action hook"
        };
        thread.SetApartmentState(ApartmentState.STA);
        actionWorker = Task.Run(RunTranslateActionsAsync);
        try
        {
            thread.Start();
            threadStarted = true;
            ready.Wait();
            if (startupError is not null)
            {
                throw new InvalidOperationException(
                    "Could not install the menu-key Translate shortcut hook.",
                    startupError);
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Menu Translate hook startup failed: {exception.Message}");
            Dispose();
            throw;
        }
    }

    /// <summary>Queue an edge validated by the remote transport; unknown keys are rejected.</summary>
    public bool TryQueueRemoteKey(string key)
    {
        ShortcutAction? action = key switch
        {
            "back" => ShortcutAction.BackDelete,
            "volume_up" => ShortcutAction.PreviousTask,
            "volume_down" => ShortcutAction.NextTask,
            _ => null
        };
        return action is not null && TryQueue(action.Value);
    }

    private bool TryQueue(ShortcutAction action)
    {
        if (Volatile.Read(ref acceptingActions) == 0)
        {
            return false;
        }

        var queued = new QueuedAction(action, GetForegroundWindow(), Environment.TickCount64);
        if (translateActions.Writer.TryWrite(queued))
        {
            return true;
        }

        Interlocked.Increment(ref droppedActionCount);
        return false;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        StopTranslateActions();
        bool quitPosted = !threadStarted || !thread.IsAlive || threadId == 0 ||
            PostThreadMessage(threadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
        int quitError = quitPosted ? 0 : Marshal.GetLastWin32Error();
        bool joined = !threadStarted || thread.Join(TimeSpan.FromSeconds(2));
        bool workerJoined = actionWorker.Wait(TimeSpan.FromSeconds(2));
        if (joined && workerJoined)
        {
            ready.Dispose();
            actionCancellation.Dispose();
        }

        if (!quitPosted || !joined || !workerJoined || !unhookSucceeded)
        {
            Console.Error.WriteLine(
                $"Menu Translate hook shutdown incomplete: quitPosted={quitPosted} " +
                $"quitError={quitError} joined={joined} workerJoined={workerJoined} " +
                $"unhooked={unhookSucceeded} " +
                $"unhookError={unhookError}. Windows will release any remaining hook " +
                "when this process exits.");
            return;
        }

        Console.WriteLine(
            $"Menu Translate hook released: blockedEvents={blockedEventCount} " +
            $"translateShortcuts={translateShortcutCount} skipped={skippedTranslateCount} " +
            $"droppedActions={droppedActionCount} deletes={deleteCount} navigation={navigationCount}. " +
            "Physical Menu and Home keys are restored.");
    }

    private void RunMessageLoop()
    {
        bool started = false;
        try
        {
            threadId = GetCurrentThreadId();
            // Ensure an immediate Dispose can post WM_QUIT after ready is set.
            PeekMessage(out _, IntPtr.Zero, 0, 0, 0);
            hook = SetWindowsHookEx(WhKeyboardLl, callback, GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "SetWindowsHookEx failed.");
            }

            started = true;
            ready.Set();
            int result;
            while ((result = GetMessage(out NativeMessage message, IntPtr.Zero, 0, 0)) > 0)
            {
                TranslateMessage(ref message);
                DispatchMessage(ref message);
            }

            if (result == -1)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "GetMessage failed.");
            }
        }
        catch (Exception exception)
        {
            if (!started)
            {
                startupError = exception;
            }

            Console.Error.WriteLine($"Menu Translate hook thread failed: {exception.Message}");
            if (started && Volatile.Read(ref disposed) == 0)
            {
                StopTranslateActions();
                Observe("按键钩子失败·请重新连接恢复");
            }
        }
        finally
        {
            if (!started)
            {
                ready.Set();
            }

            bool unexpectedExit = Volatile.Read(ref disposed) == 0 &&
                Volatile.Read(ref acceptingActions) != 0;
            StopTranslateActions();
            if (hook != IntPtr.Zero)
            {
                if (!UnhookWindowsHookEx(hook))
                {
                    unhookSucceeded = false;
                    unhookError = Marshal.GetLastWin32Error();
                    Console.Error.WriteLine(
                        $"Menu Translate hook removal failed: win32={unhookError}.");
                }

                hook = IntPtr.Zero;
            }

            if (unexpectedExit)
            {
                Observe("按键钩子已停止·请重新连接恢复");
            }
        }
    }

    private void StopTranslateActions()
    {
        Interlocked.Exchange(ref acceptingActions, 0);
        actionCancellation.Cancel();
        translateActions.Writer.TryComplete();
    }

    private async Task RunTranslateActionsAsync()
    {
        CancellationToken cancellationToken = actionCancellation.Token;
        try
        {
            await foreach (QueuedAction queued in translateActions.Reader.ReadAllAsync(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Environment.TickCount64 - queued.QueuedAt > 1000 ||
                    queued.ForegroundWindow != GetForegroundWindow())
                {
                    Observe("已跳过·窗口已切换");
                    continue;
                }

                ShortcutAction action = queued.Action;
                // Serialize Delete after Translate releases Shift, so a quick Home
                // press during the 80 ms shortcut cannot accidentally send Shift+Delete.
                if (action is ShortcutAction.Delete or ShortcutAction.BackDelete)
                {
                    if (TypelessShortcut.AnyModifierIsDown())
                    {
                        Observe("已跳过·请松开修饰键");
                        continue;
                    }

                    TypelessShortcut.SendDelete();
                    Interlocked.Increment(ref deleteCount);
                    Observe(action == ShortcutAction.BackDelete ? "返回·Delete" : "Home·Delete");
                    continue;
                }

                if (action is ShortcutAction.PreviousTask or ShortcutAction.NextTask)
                {
                    bool previous = action == ShortcutAction.PreviousTask;
                    if (TypelessShortcut.AnyModifierIsDown() ||
                        !TypelessShortcut.IsCodexForeground(queued.ForegroundWindow))
                    {
                        Observe("已跳过·请聚焦 Codex 并松开修饰键");
                        continue;
                    }

                    ActivityNavigationResult activity;
                    try
                    {
                        activity = CodexActivityNavigator.Navigate(
                            previous, queued.ForegroundWindow, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        // An unavailable external UI provider must not disable
                        // the independent Delete and Translate actions.
                        Console.Error.WriteLine($"Activity navigation unavailable: {exception.GetType().Name}.");
                        activity = ActivityNavigationResult.Unavailable;
                    }
                    if (activity != ActivityNavigationResult.NotActivityView)
                    {
                        if (activity == ActivityNavigationResult.Invoked)
                        {
                            Interlocked.Increment(ref navigationCount);
                        }

                        Observe(activity switch
                        {
                            ActivityNavigationResult.Invoked => previous
                                ? "音量＋·活动列表上一任务" : "音量－·活动列表下一任务",
                            ActivityNavigationResult.Boundary => previous
                                ? "已到活动列表顶部" : "已到已加载列表底部",
                            ActivityNavigationResult.Skipped => "已跳过·窗口或按键状态已变化",
                            _ => "已跳过·请关闭菜单并选中活动列表中的任务"
                        });
                        continue;
                    }

                    bool navigationSent = await TypelessShortcut.NavigateCodexTaskAsync(
                        previous, queued.ForegroundWindow, cancellationToken);
                    if (navigationSent)
                    {
                        Interlocked.Increment(ref navigationCount);
                        Observe(previous ? "音量＋·已发送上一任务快捷键" : "音量－·已发送下一任务快捷键");
                    }
                    else
                    {
                        Observe("已跳过·请聚焦 Codex 并松开修饰键");
                    }

                    continue;
                }

                bool sent = await TypelessShortcut.StartTranslationAsync(cancellationToken);
                if (sent)
                {
                    Interlocked.Increment(ref translateShortcutCount);
                    Observe("菜单·Typeless 翻译");
                }
                else
                {
                    Interlocked.Increment(ref skippedTranslateCount);
                    Observe("已跳过·请松开修饰键或 T");
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // StartTranslationAsync releases any accepted key-downs in its finally.
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"Remote action worker failed: {exception.GetType().Name}: {exception.Message}");
            StopTranslateActions();
            Observe("按键动作失败·请重新连接恢复");
            // A stopped sender must not leave a live hook swallowing Menu/Home.
            // The owner thread removes the hook in its finally when it sees quit.
            if (threadId != 0 && !PostThreadMessage(threadId, WmQuit, IntPtr.Zero, IntPtr.Zero))
            {
                Console.Error.WriteLine(
                    $"Could not stop Menu Translate hook after sender failure: win32={Marshal.GetLastWin32Error()}.");
            }
        }
        finally
        {
            while (translateActions.Reader.TryRead(out _))
            {
                Interlocked.Increment(ref droppedActionCount);
            }
        }
    }

    private void Observe(string text)
    {
        Console.WriteLine($"Remote action: {text}");
        Delegate[] observers = ActionObserved?.GetInvocationList() ?? [];
        foreach (Action<string> observer in observers.Cast<Action<string>>())
        {
            try
            {
                observer(text);
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"Remote action observer failed: {exception.GetType().Name}.");
            }
        }
    }

    private IntPtr OnKeyboardEvent(int code, IntPtr wParam, IntPtr lParam)
    {
        int message = unchecked((int)wParam.ToInt64());
        if (Volatile.Read(ref acceptingActions) == 0)
        {
            return CallNextHookEx(hook, code, wParam, lParam);
        }

        if (code >= 0 && message is WmKeyDown or WmKeyUp or WmSysKeyDown or WmSysKeyUp)
        {
            LowLevelKeyboardInput input = Marshal.PtrToStructure<LowLevelKeyboardInput>(lParam);
            bool injected = (input.Flags & LlkhfInjected) != 0;
            if (input.VirtualKey == VkApps && !injected)
            {
                Interlocked.Increment(ref blockedEventCount);
                bool isKeyUp = message is WmKeyUp or WmSysKeyUp;
                if (!isKeyUp && !keyDown)
                {
                    keyDown = true;
                    TryQueue(ShortcutAction.Translate);
                }
                else if (isKeyUp)
                {
                    keyDown = false;
                }

                return new IntPtr(1);
            }

            if (input.VirtualKey == VkHome && !injected)
            {
                Interlocked.Increment(ref blockedEventCount);
                bool isKeyUp = message is WmKeyUp or WmSysKeyUp;
                if (!isKeyUp && !homeKeyDown)
                {
                    homeKeyDown = true;
                    TryQueue(ShortcutAction.Delete);
                }
                else if (isKeyUp)
                {
                    homeKeyDown = false;
                }

                return new IntPtr(1);
            }
        }

        return CallNextHookEx(hook, code, wParam, lParam);
    }

    private delegate IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam);

    private enum ShortcutAction
    {
        Translate,
        Delete,
        BackDelete,
        PreviousTask,
        NextTask
    }

    private readonly record struct QueuedAction(
        ShortcutAction Action, IntPtr ForegroundWindow, long QueuedAt);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(
        int hookId,
        HookCallback callback,
        IntPtr module,
        uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(
        IntPtr hook,
        int code,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(
        uint threadId,
        uint message,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetMessage(
        out NativeMessage message,
        IntPtr window,
        uint minimumMessage,
        uint maximumMessage);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(
        out NativeMessage message,
        IntPtr window,
        uint minimumMessage,
        uint maximumMessage,
        uint removeMessage);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref NativeMessage message);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref NativeMessage message);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public IntPtr Window;
        public uint Message;
        public UIntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public Point Point;
        public uint Private;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LowLevelKeyboardInput
    {
        public uint VirtualKey;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInformation;
    }
}
