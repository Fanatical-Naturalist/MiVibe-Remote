using System.ComponentModel;
using System.Globalization;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace MiVibe.Remote.Probe;

internal static class Program
{
    private const int DefaultListenSeconds = 20;

    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        if (args.Length == 0 || args.Contains("--list", StringComparer.OrdinalIgnoreCase))
        {
            PrintDeviceList();
            return 0;
        }

        if (args.Contains("--caps", StringComparer.OrdinalIgnoreCase))
        {
            PrintTargetCapabilities();
            return 0;
        }

        int reportsIndex = Array.FindIndex(args, value =>
            value.Equals("--reports", StringComparison.OrdinalIgnoreCase));
        if (reportsIndex >= 0)
        {
            int seconds = ParseDuration(args, reportsIndex, DefaultListenSeconds);
            await CaptureTargetReportsAsync(seconds);
            return 0;
        }

        int interceptF5Index = Array.FindIndex(args, value =>
            value.Equals("--intercept-f5", StringComparison.OrdinalIgnoreCase));
        if (interceptF5Index >= 0)
        {
            int seconds = ParseDuration(args, interceptF5Index, DefaultListenSeconds);
            Listen(seconds, includeAllDevices: true, suppressF5: true);
            return 0;
        }

        if (args.Contains("--help", StringComparer.OrdinalIgnoreCase) ||
            args.Contains("-h", StringComparer.OrdinalIgnoreCase))
        {
            PrintUsage();
            return 0;
        }

        int listenIndex = Array.FindIndex(args, value =>
            value.Equals("--listen", StringComparison.OrdinalIgnoreCase));

        if (listenIndex >= 0)
        {
            int seconds = ParseDuration(args, listenIndex, DefaultListenSeconds);
            bool includeAllDevices = args.Contains("--all", StringComparer.OrdinalIgnoreCase);
            Listen(seconds, includeAllDevices, suppressF5: false);
            return 0;
        }

        Console.Error.WriteLine("Unknown arguments.");
        PrintUsage();
        return 2;
    }

    private static void PrintDeviceList()
    {
        IReadOnlyList<RawInputDevice> devices = RawInput.EnumerateDevices();

        Console.WriteLine($"Raw Input devices: {devices.Count}");
        foreach (RawInputDevice device in devices
                     .OrderByDescending(item => item.IsTargetRemote)
                     .ThenBy(item => item.Type)
                     .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            string marker = device.IsTargetRemote ? "TARGET" : "      ";
            Console.WriteLine(
                $"[{marker}] type={device.Type,-8} vid=0x{device.VendorId:X4} " +
                $"pid=0x{device.ProductId:X4} usage=0x{device.UsagePage:X2}/0x{device.Usage:X2}");
            Console.WriteLine($"         {device.Name}");
        }
    }

    private static void PrintTargetCapabilities()
    {
        RawInputDevice? target = RawInput.EnumerateDevices()
            .FirstOrDefault(device => device.IsTargetRemote);
        if (target is null)
        {
            Console.Error.WriteLine("Xiaomi VID 2717 / PID 32B8 was not found in Raw Input devices.");
            return;
        }

        Console.WriteLine($"Target: {target.Name}");
        RawInput.PrintHidCapabilities(target.Handle);
    }

    private static async Task CaptureTargetReportsAsync(int seconds)
    {
        RawInputDevice? target = RawInput.EnumerateDevices()
            .FirstOrDefault(device => device.IsTargetRemote);
        if (target is null)
        {
            Console.Error.WriteLine("Xiaomi VID 2717 / PID 32B8 was not found in Raw Input devices.");
            return;
        }

        await RawInput.CaptureRawReportsAsync(target.Handle, seconds);
    }

    private static void Listen(int seconds, bool includeAllDevices, bool suppressF5)
    {
        if (suppressF5)
        {
            Console.WriteLine(
                $"F5 interception experiment active for {seconds} seconds. " +
                "All physical F5 events are blocked only while this process is running.");
            Console.WriteLine(
                "No actions or injected keys will be executed. Press the remote microphone key, " +
                "then press the computer keyboard F5 key once.");
        }
        else
        {
            Console.WriteLine(
                includeAllDevices
                    ? $"Listening to all Raw Input and translated keyboard events for {seconds} seconds. No actions will be executed."
                    : $"Listening to Xiaomi VID 2717 / PID 32B8 for {seconds} seconds. No actions will be executed.");
            Console.WriteLine("Press physical remote buttons now...");
        }

        using var captureWindow = new RawInputCaptureWindow(includeAllDevices);
        using LowLevelKeyboardHook? translatedHook = includeAllDevices
            ? new LowLevelKeyboardHook(suppressF5)
            : null;
        using var timer = new System.Windows.Forms.Timer { Interval = seconds * 1000 };
        timer.Tick += (_, _) => Application.ExitThread();
        timer.Start();
        Application.Run();
        Console.WriteLine("Capture finished.");
    }

    private static int ParseDuration(string[] args, int optionIndex, int defaultSeconds)
    {
        if (optionIndex + 1 >= args.Length || args[optionIndex + 1].StartsWith('-'))
        {
            return defaultSeconds;
        }

        if (!int.TryParse(args[optionIndex + 1], out int seconds) || seconds is < 1 or > 600)
        {
            throw new ArgumentOutOfRangeException(
                nameof(args), "Listen duration must be between 1 and 600 seconds.");
        }

        return seconds;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("MiVibe.Remote.Probe");
        Console.WriteLine("  --list                 List Raw Input devices (default)");
        Console.WriteLine("  --caps                 Print parsed HID report capabilities for the target");
        Console.WriteLine("  --reports [seconds]    Attempt shared read-only capture of raw HID reports");
        Console.WriteLine("  --listen [seconds]     Log target remote events without executing actions");
        Console.WriteLine("  --listen [seconds] --all  Log all Raw Input plus translated keyboard events");
        Console.WriteLine("  --intercept-f5 [sec]   Temporarily block F5 while logging its Raw Input source");
    }
}

internal sealed class RawInputCaptureWindow : NativeWindow, IDisposable
{
    private readonly bool _includeAllDevices;

    public RawInputCaptureWindow(bool includeAllDevices)
    {
        _includeAllDevices = includeAllDevices;

        CreateHandle(new CreateParams
        {
            Caption = "MiVibe Remote Raw Input Probe",
            Parent = new IntPtr(-3) // HWND_MESSAGE
        });

        RawInput.RegisterForKeyboardAndConsumerControl(Handle);
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == RawInput.WmInput)
        {
            RawInputEvent? inputEvent = RawInput.ReadEvent(message.LParam);
            if (inputEvent is not null && (_includeAllDevices || inputEvent.IsTargetRemote))
            {
                Console.WriteLine(inputEvent.ToLogLine());
            }
        }
        else if (message.Msg == RawInput.WmAppCommand)
        {
            Console.WriteLine(RawInput.FormatAppCommand(message.LParam));
        }

        base.WndProc(ref message);
    }

    public void Dispose()
    {
        DestroyHandle();
    }
}

internal static class RawInput
{
    internal const int WmInput = 0x00FF;
    internal const int WmAppCommand = 0x0319;

    private const uint RidInput = 0x10000003;
    private const uint RidiDeviceName = 0x20000007;
    private const uint RidiDeviceInfo = 0x2000000B;
    private const uint RidiPreparsedData = 0x20000005;
    private const uint RimTypeKeyboard = 1;
    private const uint RimTypeHid = 2;
    private const uint RidevInputSink = 0x00000100;
    private const uint RidevDevNotify = 0x00002000;

    public static IReadOnlyList<RawInputDevice> EnumerateDevices()
    {
        uint count = 0;
        uint itemSize = (uint)Marshal.SizeOf<RawInputDeviceListEntry>();
        uint result = GetRawInputDeviceList(IntPtr.Zero, ref count, itemSize);
        ThrowIfRawInputError(result, nameof(GetRawInputDeviceList));

        if (count == 0)
        {
            return Array.Empty<RawInputDevice>();
        }

        var entries = new RawInputDeviceListEntry[count];
        result = GetRawInputDeviceList(entries, ref count, itemSize);
        ThrowIfRawInputError(result, nameof(GetRawInputDeviceList));

        var devices = new List<RawInputDevice>((int)count);
        for (int index = 0; index < count; index++)
        {
            RawInputDeviceListEntry entry = entries[index];
            string name = GetDeviceName(entry.Device);
            RawInputDeviceInfo info = GetDeviceInfo(entry.Device);

            (uint vendorId, uint productId) = DeviceIdentity.GetVendorAndProduct(
                name,
                entry.Type == RimTypeHid ? info.Union.Hid.VendorId : 0,
                entry.Type == RimTypeHid ? info.Union.Hid.ProductId : 0);

            ushort usagePage = entry.Type switch
            {
                RimTypeKeyboard => 0x01,
                0 => 0x01,
                _ => info.Union.Hid.UsagePage
            };
            ushort usage = entry.Type switch
            {
                RimTypeKeyboard => 0x06,
                0 => 0x02,
                _ => info.Union.Hid.Usage
            };

            devices.Add(new RawInputDevice(
                entry.Device,
                DescribeType(entry.Type),
                name,
                vendorId,
                productId,
                usagePage,
                usage));
        }

        return devices;
    }

    public static void RegisterForKeyboardAndConsumerControl(IntPtr windowHandle)
    {
        uint flags = RidevInputSink | RidevDevNotify;
        var devices = new[]
        {
            new RawInputDeviceRegistration(0x01, 0x06, flags, windowHandle), // keyboard
            new RawInputDeviceRegistration(0x0C, 0x01, flags, windowHandle)  // consumer control
        };

        if (!RegisterRawInputDevices(
                devices,
                (uint)devices.Length,
                (uint)Marshal.SizeOf<RawInputDeviceRegistration>()))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "RegisterRawInputDevices failed.");
        }
    }

    public static void PrintHidCapabilities(IntPtr deviceHandle)
    {
        uint preparsedSize = 0;
        uint result = GetRawInputDeviceInfo(
            deviceHandle,
            RidiPreparsedData,
            IntPtr.Zero,
            ref preparsedSize);
        ThrowIfRawInputError(result, nameof(GetRawInputDeviceInfo));

        if (preparsedSize > 0)
        {
            IntPtr rawInputPreparsedData = Marshal.AllocHGlobal(checked((int)preparsedSize));
            try
            {
                uint copiedSize = preparsedSize;
                result = GetRawInputDeviceInfo(
                    deviceHandle,
                    RidiPreparsedData,
                    rawInputPreparsedData,
                    ref copiedSize);
                ThrowIfRawInputError(result, nameof(GetRawInputDeviceInfo));
                PrintHidCapabilitiesFromPreparsedData(rawInputPreparsedData);
            }
            finally
            {
                Marshal.FreeHGlobal(rawInputPreparsedData);
            }

            return;
        }

        string deviceName = GetDeviceName(deviceHandle);
        using SafeFileHandle fileHandle = CreateFile(
            deviceName,
            0,
            0x00000001 | 0x00000002,
            IntPtr.Zero,
            3,
            0,
            IntPtr.Zero);

        if (fileHandle.IsInvalid)
        {
            Console.WriteLine(
                $"Raw Input returned no preparsed data and CreateFile failed: " +
                $"win32={Marshal.GetLastWin32Error()}");
            return;
        }

        if (!HidDGetPreparsedData(fileHandle, out IntPtr hidPreparsedData))
        {
            Console.WriteLine($"HidD_GetPreparsedData failed: win32={Marshal.GetLastWin32Error()}");
            return;
        }

        try
        {
            PrintHidCapabilitiesFromPreparsedData(hidPreparsedData);
        }
        finally
        {
            HidDFreePreparsedData(hidPreparsedData);
        }
    }

    public static async Task CaptureRawReportsAsync(IntPtr deviceHandle, int seconds)
    {
        const uint genericRead = 0x80000000;
        const uint fileShareRead = 0x00000001;
        const uint fileShareWrite = 0x00000002;
        const uint openExisting = 3;
        const uint fileFlagOverlapped = 0x40000000;

        string deviceName = GetDeviceName(deviceHandle);
        using SafeFileHandle fileHandle = CreateFile(
            deviceName,
            genericRead,
            fileShareRead | fileShareWrite,
            IntPtr.Zero,
            openExisting,
            fileFlagOverlapped,
            IntPtr.Zero);

        if (fileHandle.IsInvalid)
        {
            Console.WriteLine(
                $"Shared read-only HID open failed: win32={Marshal.GetLastWin32Error()}. " +
                "Windows commonly reserves keyboard collections for the system driver.");
            return;
        }

        if (!HidDGetPreparsedData(fileHandle, out IntPtr preparsedData))
        {
            Console.WriteLine($"HidD_GetPreparsedData failed: win32={Marshal.GetLastWin32Error()}");
            return;
        }

        int inputReportLength;
        IntPtr capsBuffer = Marshal.AllocHGlobal(64);
        try
        {
            int status = HidPGetCaps(preparsedData, capsBuffer);
            if (status != 0x00110000)
            {
                Console.WriteLine($"HidP_GetCaps failed: status=0x{status:X8}");
                return;
            }

            inputReportLength = ReadUInt16(capsBuffer, 4);
        }
        finally
        {
            Marshal.FreeHGlobal(capsBuffer);
            HidDFreePreparsedData(preparsedData);
        }

        using var stream = new FileStream(fileHandle, FileAccess.Read, inputReportLength, isAsync: true);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
        var buffer = new byte[inputReportLength];

        Console.WriteLine(
            $"Shared raw HID capture active for {seconds} seconds; " +
            $"inputReportBytes={inputReportLength}. No reports will be written.");

        try
        {
            while (!timeout.IsCancellationRequested)
            {
                int bytesRead = await stream.ReadAsync(buffer.AsMemory(), timeout.Token);
                if (bytesRead == 0)
                {
                    break;
                }

                Console.WriteLine(
                    $"{DateTimeOffset.Now:HH:mm:ss.fff} REPORT bytes={bytesRead} " +
                    $"data={Convert.ToHexString(buffer.AsSpan(0, bytesRead))}");
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when the requested capture duration elapses.
        }

        Console.WriteLine("Raw HID report capture finished.");
    }

    private static void PrintHidCapabilitiesFromPreparsedData(IntPtr preparsedData)
    {
        IntPtr capsBuffer = Marshal.AllocHGlobal(64);
        try
        {
            int status = HidPGetCaps(preparsedData, capsBuffer);
            if (status != 0x00110000)
            {
                Console.WriteLine($"HidP_GetCaps failed: status=0x{status:X8}");
                return;
            }

            ushort usage = ReadUInt16(capsBuffer, 0);
            ushort usagePage = ReadUInt16(capsBuffer, 2);
            ushort inputReportLength = ReadUInt16(capsBuffer, 4);
            ushort outputReportLength = ReadUInt16(capsBuffer, 6);
            ushort featureReportLength = ReadUInt16(capsBuffer, 8);
            ushort inputButtonCaps = ReadUInt16(capsBuffer, 46);
            ushort inputValueCaps = ReadUInt16(capsBuffer, 48);
            ushort inputDataIndices = ReadUInt16(capsBuffer, 50);

            Console.WriteLine(
                $"Collection usagePage=0x{usagePage:X4} usage=0x{usage:X4} " +
                $"inputReportBytes={inputReportLength} outputReportBytes={outputReportLength} " +
                $"featureReportBytes={featureReportLength}");
            Console.WriteLine(
                $"Input capabilities: buttonCaps={inputButtonCaps} " +
                $"valueCaps={inputValueCaps} dataIndices={inputDataIndices}");

            PrintInputButtonCapabilities(preparsedData, inputButtonCaps);
        }
        finally
        {
            Marshal.FreeHGlobal(capsBuffer);
        }
    }

    private static void PrintInputButtonCapabilities(IntPtr preparsedData, ushort requestedCount)
    {
        if (requestedCount == 0)
        {
            Console.WriteLine("No input button capabilities.");
            return;
        }

        const int buttonCapsSize = 72;
        IntPtr buffer = Marshal.AllocHGlobal(buttonCapsSize * requestedCount);
        try
        {
            ushort actualCount = requestedCount;
            int status = HidPGetButtonCaps(0, buffer, ref actualCount, preparsedData);
            if (status != 0x00110000)
            {
                Console.WriteLine($"HidP_GetButtonCaps failed: status=0x{status:X8}");
                return;
            }

            Console.WriteLine($"Input button capability records: {actualCount}");
            for (int index = 0; index < actualCount; index++)
            {
                IntPtr item = IntPtr.Add(buffer, index * buttonCapsSize);
                ushort usagePage = ReadUInt16(item, 0);
                byte reportId = Marshal.ReadByte(item, 2);
                bool isRange = Marshal.ReadByte(item, 12) != 0;
                ushort firstUsage = ReadUInt16(item, 56);
                ushort secondUsage = ReadUInt16(item, 58);

                string usages = isRange
                    ? $"usageRange=0x{firstUsage:X4}-0x{secondUsage:X4}"
                    : $"usage=0x{firstUsage:X4}";
                Console.WriteLine(
                    $"  reportId=0x{reportId:X2} usagePage=0x{usagePage:X4} {usages}");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static ushort ReadUInt16(IntPtr pointer, int offset) =>
        unchecked((ushort)Marshal.ReadInt16(pointer, offset));

    public static RawInputEvent? ReadEvent(IntPtr rawInputHandle)
    {
        uint size = 0;
        uint headerSize = (uint)Marshal.SizeOf<RawInputHeader>();
        uint result = GetRawInputData(rawInputHandle, RidInput, IntPtr.Zero, ref size, headerSize);
        ThrowIfRawInputError(result, nameof(GetRawInputData));

        IntPtr buffer = Marshal.AllocHGlobal(checked((int)size));
        try
        {
            uint copiedSize = size;
            result = GetRawInputData(rawInputHandle, RidInput, buffer, ref copiedSize, headerSize);
            ThrowIfRawInputError(result, nameof(GetRawInputData));

            RawInputHeader header = Marshal.PtrToStructure<RawInputHeader>(buffer);
            string deviceName = GetDeviceName(header.Device);
            bool isTarget = DeviceIdentity.IsTargetRemote(deviceName);
            DateTimeOffset timestamp = DateTimeOffset.Now;
            IntPtr payload = IntPtr.Add(buffer, Marshal.SizeOf<RawInputHeader>());

            if (header.Type == RimTypeKeyboard)
            {
                RawKeyboard keyboard = Marshal.PtrToStructure<RawKeyboard>(payload);
                bool isKeyUp = (keyboard.Flags & 0x01) != 0;
                return RawInputEvent.Keyboard(
                    timestamp,
                    deviceName,
                    isTarget,
                    keyboard.VirtualKey,
                    keyboard.MakeCode,
                    keyboard.Flags,
                    isKeyUp);
            }

            if (header.Type == RimTypeHid)
            {
                uint reportSize = unchecked((uint)Marshal.ReadInt32(payload));
                uint reportCount = unchecked((uint)Marshal.ReadInt32(payload, sizeof(uint)));
                int byteCount = checked((int)(reportSize * reportCount));
                var bytes = new byte[byteCount];
                Marshal.Copy(IntPtr.Add(payload, sizeof(uint) * 2), bytes, 0, byteCount);
                return RawInputEvent.Hid(timestamp, deviceName, isTarget, reportSize, reportCount, bytes);
            }

            return null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public static string FormatAppCommand(IntPtr lParam)
    {
        uint packed = unchecked((uint)lParam.ToInt64());
        int keyState = (ushort)(packed & 0xFFFF);
        int highWord = (ushort)(packed >> 16);
        int command = highWord & 0x0FFF;
        int sourceBits = highWord & 0xF000;
        string source = sourceBits switch
        {
            0x0000 => "keyboard",
            0x1000 => "oem",
            0x8000 => "mouse",
            _ => $"0x{sourceBits:X4}"
        };

        return $"{DateTimeOffset.Now:HH:mm:ss.fff} target=unknown kind=appcommand command={command} source={source} keyState=0x{keyState:X4}";
    }

    private static string GetDeviceName(IntPtr deviceHandle)
    {
        uint characterCount = 0;
        uint result = GetRawInputDeviceInfo(deviceHandle, RidiDeviceName, null, ref characterCount);
        ThrowIfRawInputError(result, nameof(GetRawInputDeviceInfo));

        if (characterCount == 0)
        {
            return "<unknown>";
        }

        var name = new StringBuilder(checked((int)characterCount));
        result = GetRawInputDeviceInfo(deviceHandle, RidiDeviceName, name, ref characterCount);
        ThrowIfRawInputError(result, nameof(GetRawInputDeviceInfo));
        return name.ToString();
    }

    private static RawInputDeviceInfo GetDeviceInfo(IntPtr deviceHandle)
    {
        var info = new RawInputDeviceInfo
        {
            Size = (uint)Marshal.SizeOf<RawInputDeviceInfo>()
        };
        uint size = info.Size;
        uint result = GetRawInputDeviceInfo(deviceHandle, RidiDeviceInfo, ref info, ref size);
        ThrowIfRawInputError(result, nameof(GetRawInputDeviceInfo));
        return info;
    }

    private static string DescribeType(uint type) => type switch
    {
        0 => "mouse",
        RimTypeKeyboard => "keyboard",
        RimTypeHid => "hid",
        _ => $"type-{type}"
    };

    private static void ThrowIfRawInputError(uint result, string operation)
    {
        if (result == uint.MaxValue)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"{operation} failed.");
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputDeviceList(
        IntPtr deviceList,
        ref uint deviceCount,
        uint entrySize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputDeviceList(
        [In, Out] RawInputDeviceListEntry[] deviceList,
        ref uint deviceCount,
        uint entrySize);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetRawInputDeviceInfo(
        IntPtr device,
        uint command,
        StringBuilder? data,
        ref uint dataSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputDeviceInfo(
        IntPtr device,
        uint command,
        ref RawInputDeviceInfo data,
        ref uint dataSize);

    [DllImport("user32.dll", EntryPoint = "GetRawInputDeviceInfoW", SetLastError = true)]
    private static extern uint GetRawInputDeviceInfo(
        IntPtr device,
        uint command,
        IntPtr data,
        ref uint dataSize);

    [DllImport("hid.dll", EntryPoint = "HidP_GetCaps")]
    private static extern int HidPGetCaps(IntPtr preparsedData, IntPtr capabilities);

    [DllImport("hid.dll", EntryPoint = "HidP_GetButtonCaps")]
    private static extern int HidPGetButtonCaps(
        int reportType,
        IntPtr buttonCaps,
        ref ushort buttonCapsLength,
        IntPtr preparsedData);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("hid.dll", EntryPoint = "HidD_GetPreparsedData", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidDGetPreparsedData(
        SafeFileHandle device,
        out IntPtr preparsedData);

    [DllImport("hid.dll", EntryPoint = "HidD_FreePreparsedData")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidDFreePreparsedData(IntPtr preparsedData);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterRawInputDevices(
        [In] RawInputDeviceRegistration[] devices,
        uint deviceCount,
        uint deviceSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputData(
        IntPtr rawInput,
        uint command,
        IntPtr data,
        ref uint dataSize,
        uint headerSize);
}

internal sealed class LowLevelKeyboardHook : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const uint LlkhfInjected = 0x00000010;
    private const uint VkF5 = 0x74;

    private readonly HookCallback _callback;
    private readonly bool _suppressF5;
    private IntPtr _hook;

    public LowLevelKeyboardHook(bool suppressF5 = false)
    {
        _suppressF5 = suppressF5;
        _callback = OnKeyboardEvent;
        _hook = SetWindowsHookEx(WhKeyboardLl, _callback, GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SetWindowsHookEx failed.");
        }
    }

    public void Dispose()
    {
        if (_hook == IntPtr.Zero)
        {
            return;
        }

        if (!UnhookWindowsHookEx(_hook))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "UnhookWindowsHookEx failed.");
        }

        _hook = IntPtr.Zero;
    }

    private IntPtr OnKeyboardEvent(int code, IntPtr wParam, IntPtr lParam)
    {
        int message = unchecked((int)wParam.ToInt64());
        if (code >= 0 && message is WmKeyDown or WmKeyUp or WmSysKeyDown or WmSysKeyUp)
        {
            LowLevelKeyboardInput input = Marshal.PtrToStructure<LowLevelKeyboardInput>(lParam);
            bool isKeyUp = message is WmKeyUp or WmSysKeyUp;
            bool isInjected = (input.Flags & LlkhfInjected) != 0;
            bool isSuppressed = _suppressF5 && input.VirtualKey == VkF5 && !isInjected;
            Console.WriteLine(
                $"{DateTimeOffset.Now:HH:mm:ss.fff} target=unknown kind=translated " +
                $"state={(isKeyUp ? "up" : "down"),4} vk=0x{input.VirtualKey:X2} " +
                $"scan=0x{input.ScanCode:X2} flags=0x{input.Flags:X2} " +
                $"injected={isInjected} suppressed={isSuppressed}");

            if (isSuppressed)
            {
                return new IntPtr(1);
            }
        }

        return CallNextHookEx(_hook, code, wParam, lParam);
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
}

internal static class DeviceIdentity
{
    private static readonly Regex StandardVidPid = new(
        @"VID_(?<vid>[0-9A-F]{4}).*PID_(?<pid>[0-9A-F]{4})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex BluetoothVidPid = new(
        @"VID&01(?<vid>[0-9A-F]{4})_PID&(?<pid>[0-9A-F]{4})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly string[] TargetFragments =
    {
        "VID_2717&PID_32B8",
        "VID&012717_PID&32B8"
    };

    public static bool IsTargetRemote(string deviceName) =>
        TargetFragments.Any(fragment =>
            deviceName.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    public static (uint VendorId, uint ProductId) GetVendorAndProduct(
        string deviceName,
        uint fallbackVendorId,
        uint fallbackProductId)
    {
        Match match = StandardVidPid.Match(deviceName);
        if (!match.Success)
        {
            match = BluetoothVidPid.Match(deviceName);
        }

        if (!match.Success)
        {
            return (fallbackVendorId, fallbackProductId);
        }

        bool parsedVendor = uint.TryParse(
            match.Groups["vid"].Value,
            NumberStyles.HexNumber,
            CultureInfo.InvariantCulture,
            out uint vendorId);
        bool parsedProduct = uint.TryParse(
            match.Groups["pid"].Value,
            NumberStyles.HexNumber,
            CultureInfo.InvariantCulture,
            out uint productId);

        return parsedVendor && parsedProduct
            ? (vendorId, productId)
            : (fallbackVendorId, fallbackProductId);
    }
}

internal sealed record RawInputDevice(
    IntPtr Handle,
    string Type,
    string Name,
    uint VendorId,
    uint ProductId,
    ushort UsagePage,
    ushort Usage)
{
    public bool IsTargetRemote =>
        (VendorId == 0x2717 && ProductId == 0x32B8) || DeviceIdentity.IsTargetRemote(Name);
}

internal sealed record RawInputEvent(
    DateTimeOffset Timestamp,
    string DeviceName,
    bool IsTargetRemote,
    string Kind,
    string Detail)
{
    public static RawInputEvent Keyboard(
        DateTimeOffset timestamp,
        string deviceName,
        bool isTargetRemote,
        ushort virtualKey,
        ushort makeCode,
        ushort flags,
        bool isKeyUp) =>
        new(
            timestamp,
            deviceName,
            isTargetRemote,
            "keyboard",
            $"state={(isKeyUp ? "up" : "down"),4} vk=0x{virtualKey:X2} scan=0x{makeCode:X2} flags=0x{flags:X2}");

    public static RawInputEvent Hid(
        DateTimeOffset timestamp,
        string deviceName,
        bool isTargetRemote,
        uint reportSize,
        uint reportCount,
        byte[] bytes) =>
        new(
            timestamp,
            deviceName,
            isTargetRemote,
            "hid",
            $"reportSize={reportSize} reportCount={reportCount} bytes={Convert.ToHexString(bytes)}");

    public string ToLogLine()
    {
        string shortName = DeviceName.Length > 96 ? $"...{DeviceName[^93..]}" : DeviceName;
        return $"{Timestamp:HH:mm:ss.fff} target={IsTargetRemote,-5} kind={Kind,-8} {Detail} device={shortName}";
    }
}

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct RawInputDeviceListEntry(IntPtr Device, uint Type);

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct RawInputDeviceRegistration(
    ushort UsagePage,
    ushort Usage,
    uint Flags,
    IntPtr TargetWindow);

[StructLayout(LayoutKind.Sequential)]
internal struct RawInputHeader
{
    public uint Type;
    public uint Size;
    public IntPtr Device;
    public IntPtr WParam;
}

[StructLayout(LayoutKind.Sequential)]
internal struct RawKeyboard
{
    public ushort MakeCode;
    public ushort Flags;
    public ushort Reserved;
    public ushort VirtualKey;
    public uint Message;
    public uint ExtraInformation;
}

[StructLayout(LayoutKind.Sequential)]
internal struct RawInputDeviceInfo
{
    public uint Size;
    public uint Type;
    public RawInputDeviceInfoUnion Union;
}

[StructLayout(LayoutKind.Explicit)]
internal struct RawInputDeviceInfoUnion
{
    [FieldOffset(0)] public RawInputMouseInfo Mouse;
    [FieldOffset(0)] public RawInputKeyboardInfo Keyboard;
    [FieldOffset(0)] public RawInputHidInfo Hid;
}

[StructLayout(LayoutKind.Sequential)]
internal struct RawInputMouseInfo
{
    public uint Id;
    public uint NumberOfButtons;
    public uint SampleRate;
    [MarshalAs(UnmanagedType.Bool)] public bool HasHorizontalWheel;
}

[StructLayout(LayoutKind.Sequential)]
internal struct RawInputKeyboardInfo
{
    public uint Type;
    public uint SubType;
    public uint KeyboardMode;
    public uint NumberOfFunctionKeys;
    public uint NumberOfIndicators;
    public uint NumberOfKeysTotal;
}

[StructLayout(LayoutKind.Sequential)]
internal struct RawInputHidInfo
{
    public uint VendorId;
    public uint ProductId;
    public uint VersionNumber;
    public ushort UsagePage;
    public ushort Usage;
}

[StructLayout(LayoutKind.Sequential)]
internal struct LowLevelKeyboardInput
{
    public uint VirtualKey;
    public uint ScanCode;
    public uint Flags;
    public uint Time;
    public UIntPtr ExtraInformation;
}
