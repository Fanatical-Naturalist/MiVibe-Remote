using System.ComponentModel;
using System.Runtime.InteropServices;

namespace MiVibe.Remote.GattProbe;

internal sealed class MenuVoiceShortcutHook : IDisposable
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
    private readonly Thread thread;
    private readonly HookCallback callback;
    private IntPtr hook;
    private uint threadId;
    private Exception? startupError;
    private bool keyDown;
    private bool homeKeyDown;
    private int blockedEventCount;
    private int voiceToggleCount;
    private int deleteCount;
    private bool unhookSucceeded = true;
    private int unhookError;
    private bool disposed;

    public MenuVoiceShortcutHook()
    {
        callback = OnKeyboardEvent;
        thread = new Thread(RunMessageLoop)
        {
            IsBackground = true,
            Name = "MiVibe menu-key Voice shortcut hook"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();

        if (startupError is not null)
        {
            throw new InvalidOperationException(
                "Could not install the menu-key Voice shortcut hook.",
                startupError);
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        bool quitPosted = threadId == 0 ||
            PostThreadMessage(threadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
        int quitError = quitPosted ? 0 : Marshal.GetLastWin32Error();
        bool joined = thread.Join(TimeSpan.FromSeconds(2));
        ready.Dispose();

        if (!quitPosted || !joined || !unhookSucceeded)
        {
            Console.Error.WriteLine(
                $"Menu Voice hook shutdown incomplete: quitPosted={quitPosted} " +
                $"quitError={quitError} joined={joined} unhooked={unhookSucceeded} " +
                $"unhookError={unhookError}. Windows will release any remaining hook " +
                "when this process exits.");
            return;
        }

        Console.WriteLine(
            $"Menu Voice hook released: blockedEvents={blockedEventCount} " +
            $"voiceToggles={voiceToggleCount} deletes={deleteCount}. " +
            "Physical Menu and Home keys are restored.");
    }

    private void RunMessageLoop()
    {
        try
        {
            threadId = GetCurrentThreadId();
            hook = SetWindowsHookEx(WhKeyboardLl, callback, GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "SetWindowsHookEx failed.");
            }
        }
        catch (Exception exception)
        {
            startupError = exception;
        }
        finally
        {
            ready.Set();
        }

        if (startupError is not null)
        {
            return;
        }

        while (GetMessage(out NativeMessage message, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref message);
            DispatchMessage(ref message);
        }

        if (hook != IntPtr.Zero)
        {
            if (!UnhookWindowsHookEx(hook))
            {
                unhookSucceeded = false;
                unhookError = Marshal.GetLastWin32Error();
            }

            hook = IntPtr.Zero;
        }
    }

    private IntPtr OnKeyboardEvent(int code, IntPtr wParam, IntPtr lParam)
    {
        int message = unchecked((int)wParam.ToInt64());
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
                    try
                    {
                        TypelessShortcut.ToggleCodexVoice();
                        Interlocked.Increment(ref voiceToggleCount);
                        Console.WriteLine(
                            "Menu key pressed: Ctrl+Alt+* sent to Codex Voice.");
                    }
                    catch (Exception exception)
                    {
                        Console.Error.WriteLine(
                            $"Codex Voice shortcut failed: {exception.GetType().Name}: " +
                            exception.Message);
                    }
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
                    try
                    {
                        TypelessShortcut.SendDelete();
                        Interlocked.Increment(ref deleteCount);
                        Console.WriteLine("Home key pressed: one Delete sent.");
                    }
                    catch (Exception exception)
                    {
                        Console.Error.WriteLine(
                            $"Delete injection failed: {exception.GetType().Name}: " +
                            exception.Message);
                    }
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
