using System.ComponentModel;
using System.Runtime.InteropServices;

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
                    Flags = KeyEventScanCode | flags
                }
            }
        };

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(
        uint inputCount,
        [In] Input[] inputs,
        int inputSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Union;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
        [FieldOffset(0)] public HardwareInput Hardware;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInformation;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInformation;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HardwareInput
    {
        public uint Message;
        public ushort ParameterLow;
        public ushort ParameterHigh;
    }
}
