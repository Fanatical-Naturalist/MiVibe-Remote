using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace MiVibe.Remote.GattProbe;

internal static class TypelessShortcut
{
    private const uint InputKeyboard = 1;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint KeyEventExtendedKey = 0x0001;
    private const uint KeyEventScanCode = 0x0008;
    private const ushort ScanRightControl = 0x1D;
    private const ushort ScanRightShift = 0x36;
    private const ushort ScanLeftControl = 0x1D;
    private const ushort ScanLeftAlt = 0x38;
    private const ushort ScanLeftShift = 0x2A;
    private const ushort ScanDigit8 = 0x09;
    private const ushort ScanDelete = 0x53;
    private const ushort ScanBackspace = 0x0E;
    private const ushort ScanT = 0x14;
    private const ushort ScanPageUp = 0x49;
    private const ushort ScanPageDown = 0x51;
    private const string CodexPackageFamily = "OpenAI.Codex_2p2nqsd0c76g0";
    private static readonly UIntPtr InjectionMarker = new(0x4D56544EU);
    private static readonly int[] ModifierGuardKeys =
    [
        0xA0, 0xA1, // left/right Shift
        0xA2, 0xA3, // left/right Control
        0xA4, 0xA5, // left/right Alt
        0x5B, 0x5C  // left/right Windows
    ];

    public static bool AnyModifierIsDown() =>
        ModifierGuardKeys.Any(key => (GetAsyncKeyState(key) & 0x8000) != 0);

    public static async Task<bool> StartTranslationAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (AnyModifierIsDown() || (GetAsyncKeyState(0x54) & 0x8000) != 0)
        {
            return false;
        }

        Input[] keyDownInputs =
        [
            CreateScanCodeInput(ScanRightShift, 0),
            CreateScanCodeInput(ScanT, 0)
        ];
        uint sent = 0;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            sent = SendInput((uint)keyDownInputs.Length, keyDownInputs, Marshal.SizeOf<Input>());
            if (sent != keyDownInputs.Length)
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    $"Translate shortcut key-down was incomplete: sent={sent}/2.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(80), cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally
        {
            // Cancellation must shorten the hold, never skip the matching key-ups.
            // A partial key-down only needs releases for events Windows accepted.
            ReleaseAcceptedKeys(sent, [(ScanRightShift, 0), (ScanT, 0)]);
        }
    }

    public static Task<bool> NavigateCodexTaskAsync(
        bool previous, IntPtr expectedForeground, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        int virtualKey = previous ? 0x21 : 0x22;
        if (AnyModifierIsDown() || (GetAsyncKeyState(virtualKey) & 0x8000) != 0 ||
            !IsCodexForeground(expectedForeground))
        {
            return Task.FromResult(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        // Ordinary-view fallback uses the default Ctrl+Page shortcuts.
        // Recheck after process inspection; never activate or refocus a window.
        if (GetForegroundWindow() != expectedForeground || AnyModifierIsDown())
        {
            return Task.FromResult(false);
        }

        SendCodexNavigation(previous, cancellationToken,
            inputs => SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()));
        return Task.FromResult(true);
    }

    internal static void SendCodexNavigation(
        bool previous, CancellationToken cancellationToken, Func<Input[], uint> sender)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ushort pageScan = previous ? ScanPageUp : ScanPageDown;
        Input[] inputs =
        [
            CreateScanCodeInput(ScanLeftControl, 0),
            CreateScanCodeInput(pageScan, KeyEventExtendedKey),
            CreateScanCodeInput(pageScan, KeyEventExtendedKey | KeyEventKeyUp),
            CreateScanCodeInput(ScanLeftControl, KeyEventKeyUp)
        ];
        uint sent = 0;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            // One batch prevents other input from interleaving with held modifiers.
            sent = sender(inputs);
            if (sent != inputs.Length)
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    $"Codex navigation shortcut was incomplete: sent={sent}/4.");
            }
        }
        finally
        {
            // Only release accepted downs that have not yet received their ups.
            // Prefix lengths 0..4 leave 0,1,2,1,0 keys held respectively.
            uint accepted = Math.Min(sent, (uint)inputs.Length);
            uint pending = Math.Min(accepted, (uint)inputs.Length - accepted);
            ReleaseAcceptedKeys(pending,
                [(ScanLeftControl, 0), (pageScan, KeyEventExtendedKey)], sender);
        }
    }

    internal static bool IsCodexForeground(IntPtr expectedWindow)
    {
        if (expectedWindow == IntPtr.Zero || GetForegroundWindow() != expectedWindow)
        {
            return false;
        }

        GetWindowThreadProcessId(expectedWindow, out uint processId);
        using SafeProcessHandle process = OpenProcess(0x1000, false, processId);
        if (process.IsInvalid)
        {
            return false;
        }

        var image = new StringBuilder(32768);
        uint imageLength = (uint)image.Capacity;
        if (!QueryFullProcessImageName(process, 0, image, ref imageLength))
        {
            return false;
        }

        string path = image.ToString();
        string file = Path.GetFileName(path);
        string? appDirectory = Path.GetDirectoryName(path);
        string? packageDirectory = appDirectory is null ? null : Path.GetDirectoryName(appDirectory);
        if (!(file.Equals("ChatGPT.exe", StringComparison.OrdinalIgnoreCase) ||
              file.Equals("codex.exe", StringComparison.OrdinalIgnoreCase)) ||
            !string.Equals(Path.GetFileName(appDirectory), "app", StringComparison.OrdinalIgnoreCase) ||
            !(Path.GetFileName(packageDirectory)?.StartsWith("OpenAI.Codex_", StringComparison.OrdinalIgnoreCase) ?? false))
        {
            return false;
        }

        var family = new StringBuilder(256);
        uint familyLength = (uint)family.Capacity;
        return GetPackageFamilyName(process, ref familyLength, family) == 0 &&
            family.ToString().Equals(CodexPackageFamily, StringComparison.OrdinalIgnoreCase) &&
            GetForegroundWindow() == expectedWindow;
    }

    private static void ReleaseAcceptedKeys(
        uint acceptedKeyDowns, (ushort Scan, uint Flags)[] keyDowns,
        Func<Input[], uint>? sender = null)
    {
        int? firstError = null;
        foreach ((ushort scan, uint flags) in keyDowns.Take((int)acceptedKeyDowns).Reverse())
        {
            Input[] release = [CreateScanCodeInput(scan, flags | KeyEventKeyUp)];
            bool released = false;
            int lastError = 0;
            for (int attempt = 0; attempt < 3 && !released; attempt++)
            {
                released = (sender is null
                    ? SendInput(1, release, Marshal.SizeOf<Input>())
                    : sender(release)) == 1;
                if (!released)
                {
                    lastError = Marshal.GetLastWin32Error();
                }
            }

            if (!released)
            {
                firstError ??= lastError;
                Console.Error.WriteLine(
                    $"Shortcut key-up failed: scan=0x{scan:X2} win32={lastError}.");
            }
        }

        if (firstError is not null)
        {
            throw new Win32Exception(
                firstError.Value,
                "Shortcut cleanup could not release every injected key.");
        }
    }

    public static void ToggleDictation()
    {
        Input[] inputs =
        [
            CreateScanCodeInput(ScanRightControl, KeyEventExtendedKey),
            CreateScanCodeInput(ScanRightShift, 0),
            CreateScanCodeInput(ScanRightShift, KeyEventKeyUp),
            CreateScanCodeInput(ScanRightControl, KeyEventExtendedKey | KeyEventKeyUp)
        ];

        Input[] cleanupInputs =
        [
            CreateScanCodeInput(ScanRightShift, KeyEventKeyUp),
            CreateScanCodeInput(ScanRightControl, KeyEventExtendedKey | KeyEventKeyUp)
        ];

        Send(inputs, cleanupInputs);
    }

    public static void ToggleCodexVoice()
    {
        Input[] inputs =
        [
            CreateScanCodeInput(ScanLeftControl, 0),
            CreateScanCodeInput(ScanLeftAlt, 0),
            CreateScanCodeInput(ScanLeftShift, 0),
            CreateScanCodeInput(ScanDigit8, 0),
            CreateScanCodeInput(ScanDigit8, KeyEventKeyUp),
            CreateScanCodeInput(ScanLeftShift, KeyEventKeyUp),
            CreateScanCodeInput(ScanLeftAlt, KeyEventKeyUp),
            CreateScanCodeInput(ScanLeftControl, KeyEventKeyUp)
        ];

        Input[] cleanupInputs =
        [
            CreateScanCodeInput(ScanDigit8, KeyEventKeyUp),
            CreateScanCodeInput(ScanLeftShift, KeyEventKeyUp),
            CreateScanCodeInput(ScanLeftAlt, KeyEventKeyUp),
            CreateScanCodeInput(ScanLeftControl, KeyEventKeyUp)
        ];

        Send(inputs, cleanupInputs);
    }

    public static void SendDelete()
    {
        Input[] inputs =
        [
            CreateScanCodeInput(ScanDelete, KeyEventExtendedKey),
            CreateScanCodeInput(ScanDelete, KeyEventExtendedKey | KeyEventKeyUp)
        ];

        Input[] cleanupInputs =
        [
            CreateScanCodeInput(ScanDelete, KeyEventExtendedKey | KeyEventKeyUp)
        ];

        Send(inputs, cleanupInputs);
    }

    public static void SendBackspace()
    {
        Input[] inputs =
        [
            CreateScanCodeInput(ScanBackspace, 0),
            CreateScanCodeInput(ScanBackspace, KeyEventKeyUp)
        ];
        Input[] cleanupInputs = [CreateScanCodeInput(ScanBackspace, KeyEventKeyUp)];
        Send(inputs, cleanupInputs);
    }

    private static void Send(Input[] inputs, Input[] cleanupInputs)
    {
        uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        if (sent != inputs.Length)
        {
            int error = Marshal.GetLastWin32Error();
            _ = SendInput(
                (uint)cleanupInputs.Length,
                cleanupInputs,
                Marshal.SizeOf<Input>());
            throw new Win32Exception(error, "SendInput failed.");
        }
    }

    private static Input CreateScanCodeInput(ushort scanCode, uint flags) =>
        new()
        {
            Type = InputKeyboard,
            Union = new InputUnion
            {
                Keyboard = new KeyboardInput
                {
                    ScanCode = scanCode,
                    Flags = KeyEventScanCode | flags,
                    ExtraInformation = InjectionMarker
                }
            }
        };

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(
        uint inputCount,
        [In] Input[] inputs,
        int inputSize);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(
        uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(
        SafeProcessHandle process, uint flags, StringBuilder image, ref uint size);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetPackageFamilyName(
        SafeProcessHandle process, ref uint length, StringBuilder familyName);

    [StructLayout(LayoutKind.Sequential)]
    internal struct Input
    {
        public uint Type;
        public InputUnion Union;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
        [FieldOffset(0)] public HardwareInput Hardware;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInformation;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInformation;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct HardwareInput
    {
        public uint Message;
        public ushort ParameterLow;
        public ushort ParameterHigh;
    }
}
