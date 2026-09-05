using System.Diagnostics;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Security.Cryptography;

namespace MiVibe.Remote.GattProbe;

internal static class Program
{
    private const string DefaultDeviceName = "小米蓝牙语音遥控器";
    private const string FirmwareDeviceName = "MI RC";
    private const int DefaultListenSeconds = 45;
    private const int DefaultVoiceCaptureSeconds = 8;
    private const string DefaultRenderDeviceName = "CABLE Input";
    private const string ResidentMutexName =
        "Local\\MiVibe.Remote.GattProbe-2717-32B8";

    private static readonly HashSet<Guid> ListenServiceAllowlist =
    [
        Guid.Parse("8A7A0001-2C42-C2A2-0F36-41928C259B78"),
        Guid.Parse("AB5E0001-5A21-4F05-BC7D-AF01F617B664")
    ];

    private static readonly string[] DefaultDeviceNames =
    [
        DefaultDeviceName,
        FirmwareDeviceName
    ];

    [STAThread]
    private static async Task<int> Main(string[] args)
    {
        try
        {
            return await RunAsync(args);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"MiVibe Remote could not start: {exception.Message}");
            return 9;
        }
    }

    private static async Task<int> RunAsync(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        if (args.Contains("--help", StringComparer.OrdinalIgnoreCase) ||
            args.Contains("-h", StringComparer.OrdinalIgnoreCase))
        {
            PrintUsage();
            return 0;
        }

        if (args.Contains("--shortcut-test", StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine("Focus a text field now. Typeless will toggle ON in 5 seconds...");
            await Task.Delay(TimeSpan.FromSeconds(5));
            TypelessShortcut.ToggleDictation();
            Console.WriteLine("RightCtrl+RightShift scan-code shortcut sent. Waiting 5 seconds...");
            await Task.Delay(TimeSpan.FromSeconds(5));
            TypelessShortcut.ToggleDictation();
            Console.WriteLine("RightCtrl+RightShift scan-code shortcut sent again to toggle OFF.");
            return 0;
        }

        if (args.Contains("--codex-voice-shortcut-test", StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine(
                "Focus Codex now. Ctrl+Alt+* will be sent in 5 seconds...");
            await Task.Delay(TimeSpan.FromSeconds(5));
            TypelessShortcut.ToggleCodexVoice();
            Console.WriteLine("Ctrl+Alt+* scan-code shortcut sent once.");
            return 0;
        }

        if (args.Contains("--audio-status", StringComparer.OrdinalIgnoreCase))
        {
            AudioRouteInspector.InspectAndPrint();
            return 0;
        }

        int? menuVoiceTestSeconds = ParseDurationOption(
            args,
            "--menu-translate-test",
            20,
            5,
            60) ?? ParseDurationOption(
            args,
            "--menu-voice-test",
            20,
            5,
            60);
        if (menuVoiceTestSeconds is not null)
        {
            Console.WriteLine(
                $"Temporary Menu Translate test active for {menuVoiceTestSeconds} seconds. " +
                "Press Menu to send RightShift+T, or press Home for one Delete. " +
                "The computer backtick remains available; computer Home is reserved during the test.");
            using var menuVoiceHook = new MenuVoiceShortcutHook();
            await Task.Delay(TimeSpan.FromSeconds(menuVoiceTestSeconds.Value));
            return 0;
        }

        string? requestedDeviceName = GetOption(args, "--name");
        IReadOnlyList<string> acceptedDeviceNames = requestedDeviceName is null
            ? DefaultDeviceNames
            : [requestedDeviceName];
        int? listenSeconds = ParseListenSeconds(args);
        int? voiceCaptureSeconds = ParseDurationOption(
            args,
            "--voice-capture",
            DefaultVoiceCaptureSeconds,
            1,
            900);
        int? voiceLiveSeconds = ParseDurationOption(
            args,
            "--voice-live",
            DefaultVoiceCaptureSeconds,
            1,
            900);
        int? typelessLiveSeconds = ParseDurationOption(
            args,
            "--typeless-live",
            DefaultVoiceCaptureSeconds,
            1,
            900);
        int? codexVoiceLiveSeconds = ParseDurationOption(
            args,
            "--codex-voice-live",
            DefaultVoiceCaptureSeconds,
            1,
            900);
        int? menuCodexVoiceLiveSeconds = ParseDurationOption(
            args,
            "--menu-translate-live",
            DefaultVoiceCaptureSeconds,
            1,
            900) ?? ParseDurationOption(
            args,
            "--menu-codex-voice-live",
            DefaultVoiceCaptureSeconds,
            1,
            900);
        bool residentMode = args.Contains("--resident", StringComparer.OrdinalIgnoreCase);
        bool externalKeyController = args.Contains("--external-key-controller", StringComparer.OrdinalIgnoreCase);
        if (externalKeyController && !residentMode)
        {
            Console.Error.WriteLine("--external-key-controller is valid only with --resident.");
            return 2;
        }

        string? parentProcessIdValue = GetOption(args, "--parent-pid");
        int? parentProcessId = null;
        if (parentProcessIdValue is not null)
        {
            if (!int.TryParse(parentProcessIdValue, out int parsedParentProcessId) ||
                parsedParentProcessId <= 0)
            {
                Console.Error.WriteLine("--parent-pid must be a positive process ID.");
                return 2;
            }

            parentProcessId = parsedParentProcessId;
        }

        if (parentProcessId is not null && !residentMode)
        {
            Console.Error.WriteLine("--parent-pid is valid only with --resident.");
            return 2;
        }

        double gainDb = ParseGainDb(args);

        int selectedModes = (listenSeconds is not null ? 1 : 0) +
            (voiceCaptureSeconds is not null ? 1 : 0) +
            (voiceLiveSeconds is not null ? 1 : 0) +
            (typelessLiveSeconds is not null ? 1 : 0) +
            (codexVoiceLiveSeconds is not null ? 1 : 0) +
            (menuCodexVoiceLiveSeconds is not null ? 1 : 0) +
            (residentMode ? 1 : 0);
        if (selectedModes > 1)
        {
            Console.Error.WriteLine(
                "Choose only one of --listen, --voice-capture, --voice-live, " +
                "--typeless-live, --codex-voice-live, --menu-translate-live, or --resident.");
            return 2;
        }

        bool residentMutexCreated = true;
        using Mutex? residentMutex = residentMode
            ? new Mutex(
                initiallyOwned: false,
                ResidentMutexName,
                out residentMutexCreated)
            : null;
        if (!residentMutexCreated)
        {
            Console.Error.WriteLine("The resident voice bridge is already running.");
            return 7;
        }

        Console.WriteLine(
            $"Finding paired BLE device: {string.Join(" | ", acceptedDeviceNames)}");
        DeviceInformation? deviceInfo = await FindPairedDeviceAsync(acceptedDeviceNames);
        if (deviceInfo is null)
        {
            Console.Error.WriteLine("Target paired BLE device was not found.");
            return 3;
        }

        using BluetoothLEDevice? device = await BluetoothLEDevice.FromIdAsync(deviceInfo.Id);
        if (device is null)
        {
            Console.Error.WriteLine("Windows found the device but could not open a BLE connection.");
            return 4;
        }

        Console.WriteLine($"Connected: name={device.Name} status={device.ConnectionStatus}");
        GattDeviceServicesResult servicesResult =
            await device.GetGattServicesAsync(BluetoothCacheMode.Uncached);

        if (servicesResult.Status != GattCommunicationStatus.Success)
        {
            Console.Error.WriteLine(
                $"Service discovery failed: status={servicesResult.Status} " +
                $"protocolError={servicesResult.ProtocolError?.ToString() ?? "none"}");
            return 5;
        }

        var notificationCandidates = new List<NotificationCandidate>();
        Console.WriteLine($"Services: {servicesResult.Services.Count}");

        foreach (GattDeviceService service in servicesResult.Services.OrderBy(item => item.Uuid))
        {
            await PrintServiceAsync(service, notificationCandidates);
        }

        if (voiceCaptureSeconds is not null ||
            voiceLiveSeconds is not null ||
            typelessLiveSeconds is not null ||
            codexVoiceLiveSeconds is not null ||
            menuCodexVoiceLiveSeconds is not null ||
            residentMode)
        {
            string outputPath = GetOption(args, "--out") ??
                Path.Combine(
                    "logs",
                    $"atvv-voice-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.wav");
            string? renderDeviceName = voiceLiveSeconds is not null ||
                                       typelessLiveSeconds is not null ||
                                       codexVoiceLiveSeconds is not null ||
                                       menuCodexVoiceLiveSeconds is not null ||
                                       residentMode
                ? GetOption(args, "--render-device") ?? DefaultRenderDeviceName
                : null;

            if (residentMode)
            {
                AudioRouteInspector.InspectAndPrint();
                using var cancellation = new CancellationTokenSource();
                EventWaitHandle? shutdownEvent = null;
                RegisteredWaitHandle? shutdownRegistration = null;
                Process? parentProcess = null;
                Task? parentExitTask = null;
                string? shutdownEventName = GetOption(args, "--shutdown-event");
                if (shutdownEventName is not null)
                {
                    shutdownEvent = EventWaitHandle.OpenExisting(shutdownEventName);
                    shutdownRegistration = ThreadPool.RegisterWaitForSingleObject(
                        shutdownEvent,
                        (_, _) => cancellation.Cancel(),
                        state: null,
                        Timeout.Infinite,
                        executeOnlyOnce: true);
                    Console.WriteLine("Tray shutdown signal connected.");
                }

                ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
                {
                    eventArgs.Cancel = true;
                    cancellation.Cancel();
                    Console.WriteLine("Shutdown requested. Restoring hooks and closing the ATVV session...");
                };
                Console.CancelKeyPress += cancelHandler;
                try
                {
                    if (parentProcessId is not null)
                    {
                        try
                        {
                            parentProcess = Process.GetProcessById(parentProcessId.Value);
                        }
                        catch (ArgumentException)
                        {
                            Console.Error.WriteLine(
                                $"Tray parent process {parentProcessId.Value} is no longer running.");
                            return 8;
                        }

                        parentExitTask = WatchParentExitAsync(parentProcess, cancellation);
                        Console.WriteLine(
                            $"Tray parent process monitor connected: pid={parentProcessId.Value}.");
                    }

                    await using var batteryMonitor = new BatteryMonitor(servicesResult.Services);
                    await batteryMonitor.StartAsync(cancellation.Token);
                    return await AtvvVoiceCapture.RunResidentAsync(
                        device,
                        servicesResult.Services,
                        gainDb,
                        renderDeviceName!,
                        cancellation.Token,
                        externalKeyController);
                }
                finally
                {
                    cancellation.Cancel();
                    if (parentExitTask is not null)
                    {
                        await parentExitTask;
                    }

                    parentProcess?.Dispose();
                    Console.CancelKeyPress -= cancelHandler;
                    shutdownRegistration?.Unregister(waitObject: null);
                    shutdownEvent?.Dispose();
                }
            }

            return await AtvvVoiceCapture.RunAsync(
                device,
                servicesResult.Services,
                voiceCaptureSeconds ?? voiceLiveSeconds ?? typelessLiveSeconds ??
                     codexVoiceLiveSeconds ?? menuCodexVoiceLiveSeconds!.Value,
                outputPath,
                gainDb,
                renderDeviceName,
                typelessLiveSeconds is not null,
                codexVoiceLiveSeconds is not null,
                menuCodexVoiceLiveSeconds is not null);
        }

        if (listenSeconds is null)
        {
            Console.WriteLine("Enumeration finished. No notification descriptors were changed.");
            return 0;
        }

        if (notificationCandidates.Count == 0)
        {
            Console.WriteLine("No notifiable or indicatable characteristics were accessible.");
            return 6;
        }

        await ListenAsync(notificationCandidates, listenSeconds.Value);
        return 0;
    }

    private static async Task<DeviceInformation?> FindPairedDeviceAsync(
        IReadOnlyCollection<string> acceptedNames)
    {
        string selector = BluetoothLEDevice.GetDeviceSelectorFromPairingState(true);
        DeviceInformationCollection devices = await DeviceInformation.FindAllAsync(selector);

        return devices.FirstOrDefault(device =>
            acceptedNames.Contains(device.Name, StringComparer.OrdinalIgnoreCase));
    }

    private static async Task PrintServiceAsync(
        GattDeviceService service,
        ICollection<NotificationCandidate> notificationCandidates)
    {
        Console.WriteLine($"SERVICE {service.Uuid:D}");
        GattCharacteristicsResult characteristicsResult =
            await service.GetCharacteristicsAsync(BluetoothCacheMode.Uncached);

        if (characteristicsResult.Status != GattCommunicationStatus.Success)
        {
            Console.WriteLine(
                $"  inaccessible status={characteristicsResult.Status} " +
                $"protocolError={characteristicsResult.ProtocolError?.ToString() ?? "none"}");
            return;
        }

        foreach (GattCharacteristic characteristic in
                 characteristicsResult.Characteristics.OrderBy(item => item.Uuid))
        {
            GattCharacteristicProperties properties = characteristic.CharacteristicProperties;
            Console.WriteLine($"  CHARACTERISTIC {characteristic.Uuid:D} properties={properties}");

            GattDescriptorsResult descriptorsResult =
                await characteristic.GetDescriptorsAsync(BluetoothCacheMode.Uncached);
            if (descriptorsResult.Status == GattCommunicationStatus.Success)
            {
                foreach (GattDescriptor descriptor in descriptorsResult.Descriptors.OrderBy(item => item.Uuid))
                {
                    Console.WriteLine($"    DESCRIPTOR {descriptor.Uuid:D}");
                }
            }
            else
            {
                Console.WriteLine($"    descriptors inaccessible status={descriptorsResult.Status}");
            }

            bool canNotify = properties.HasFlag(GattCharacteristicProperties.Notify);
            bool canIndicate = properties.HasFlag(GattCharacteristicProperties.Indicate);
            if ((canNotify || canIndicate) && ListenServiceAllowlist.Contains(service.Uuid))
            {
                notificationCandidates.Add(new NotificationCandidate(
                    service.Uuid,
                    characteristic,
                    canNotify
                        ? GattClientCharacteristicConfigurationDescriptorValue.Notify
                        : GattClientCharacteristicConfigurationDescriptorValue.Indicate));
            }
        }
    }

    private static async Task ListenAsync(
        IReadOnlyCollection<NotificationCandidate> candidates,
        int listenSeconds)
    {
        var active = new List<NotificationCandidate>();

        foreach (NotificationCandidate candidate in candidates)
        {
            candidate.Characteristic.ValueChanged += candidate.OnValueChanged;
            GattCommunicationStatus status;
            try
            {
                status = await candidate.Characteristic
                    .WriteClientCharacteristicConfigurationDescriptorAsync(candidate.SubscriptionMode);
            }
            catch (Exception exception)
            {
                candidate.Characteristic.ValueChanged -= candidate.OnValueChanged;
                Console.WriteLine(
                    $"SUBSCRIBE service={candidate.ServiceUuid:D} char={candidate.Characteristic.Uuid:D} " +
                    $"error={exception.GetType().Name}: {exception.Message}");
                continue;
            }

            Console.WriteLine(
                $"SUBSCRIBE service={candidate.ServiceUuid:D} char={candidate.Characteristic.Uuid:D} " +
                $"mode={candidate.SubscriptionMode} status={status}");

            if (status == GattCommunicationStatus.Success)
            {
                active.Add(candidate);
            }
            else
            {
                candidate.Characteristic.ValueChanged -= candidate.OnValueChanged;
            }
        }

        if (active.Count == 0)
        {
            Console.WriteLine("Windows did not allow any notification subscription.");
            return;
        }

        Console.WriteLine($"Listening for {listenSeconds} seconds. Press only the requested remote buttons...");
        await Task.Delay(TimeSpan.FromSeconds(listenSeconds));

        foreach (NotificationCandidate candidate in active)
        {
            try
            {
                await candidate.Characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                    GattClientCharacteristicConfigurationDescriptorValue.None);
            }
            catch (Exception exception)
            {
                Console.WriteLine(
                    $"UNSUBSCRIBE service={candidate.ServiceUuid:D} char={candidate.Characteristic.Uuid:D} " +
                    $"error={exception.GetType().Name}: {exception.Message}");
            }
            finally
            {
                candidate.Characteristic.ValueChanged -= candidate.OnValueChanged;
            }
        }

        Console.WriteLine("Notification capture finished.");
    }

    private static int? ParseListenSeconds(string[] args)
    {
        return ParseDurationOption(args, "--listen", DefaultListenSeconds, 1, 600);
    }

    private static int? ParseDurationOption(
        string[] args,
        string option,
        int defaultSeconds,
        int minimumSeconds,
        int maximumSeconds)
    {
        int index = Array.FindIndex(args, value =>
            value.Equals(option, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return null;
        }

        if (index + 1 >= args.Length || args[index + 1].StartsWith('-'))
        {
            return defaultSeconds;
        }

        if (!int.TryParse(args[index + 1], out int seconds) ||
            seconds < minimumSeconds ||
            seconds > maximumSeconds)
        {
            throw new ArgumentOutOfRangeException(
                nameof(args),
                $"{option} duration must be between {minimumSeconds} and {maximumSeconds} seconds.");
        }

        return seconds;
    }

    private static string? GetOption(string[] args, string option)
    {
        int index = Array.FindIndex(args, value =>
            value.Equals(option, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static async Task WatchParentExitAsync(
        Process parentProcess,
        CancellationTokenSource lifetime)
    {
        try
        {
            await parentProcess.WaitForExitAsync(lifetime.Token);
            Console.WriteLine(
                $"Tray parent process {parentProcess.Id} exited. Shutting down the resident bridge...");
            lifetime.Cancel();
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
            // Normal tray-requested shutdown stops the parent watcher too.
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"Tray parent monitor failed: {exception.GetType().Name}: {exception.Message}");
            lifetime.Cancel();
        }
    }

    private static double ParseGainDb(string[] args)
    {
        string? value = GetOption(args, "--gain-db");
        if (value is null)
        {
            return 12.0;
        }

        if (!double.TryParse(
                value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out double gainDb) || gainDb is < -12.0 or > 24.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(args), "--gain-db must be between -12 and 24.");
        }

        return gainDb;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("MiVibe.Remote.GattProbe");
        Console.WriteLine("  --list                 Enumerate services and characteristics (default)");
        Console.WriteLine("  --listen [seconds]     Subscribe to accessible Notify/Indicate characteristics");
        Console.WriteLine("  --voice-capture [sec]  Run documented ATVV handshake and capture microphone audio");
        Console.WriteLine("  --voice-live [sec]     Capture and stream decoded PCM to VB-CABLE in real time");
        Console.WriteLine("  --typeless-live [sec]  Voice live plus automatic Typeless push-to-talk shortcut");
        Console.WriteLine("  --codex-voice-live [sec]  Voice live plus opening Codex Voice after bridge warm-up");
        Console.WriteLine("  --menu-translate-live [sec]  Voice live plus Menu-key Typeless Translate (RightShift+T)");
        Console.WriteLine("  --resident             Run the resident voice bridge until Ctrl+C");
        Console.WriteLine("  --external-key-controller  With --resident, leave Menu/Home actions to the tray host");
        Console.WriteLine("  --audio-status         Inspect CABLE Output and AirPods input/output routing");
        Console.WriteLine("  --shutdown-event <name>  Internal graceful-stop signal used by the tray app");
        Console.WriteLine("  --parent-pid <pid>      Internal parent lifetime monitor used by the tray app");
        Console.WriteLine("  --shortcut-test        Toggle Typeless on after 5 sec, then off after another 5 sec");
        Console.WriteLine("  --codex-voice-shortcut-test  Send Ctrl+Alt+* once after 5 sec");
        Console.WriteLine(
            "  --menu-translate-test [sec]  Temporarily map Menu to Translate and Home to Delete");
        Console.WriteLine("  Legacy --menu-voice-test / --menu-codex-voice-live names remain aliases for Translate modes.");
        Console.WriteLine("  --out <wav path>       WAV output path for --voice-capture");
        Console.WriteLine("  --render-device <name> Playback endpoint for --voice-live (default: CABLE Input)");
        Console.WriteLine("  --gain-db <value>      PCM gain from -12 to +24 dB (default: +12 dB)");
        Console.WriteLine("  --name <device name>   Override the paired device name");
        Console.WriteLine();
        Console.WriteLine("List mode never writes characteristic values.");
        Console.WriteLine("Listen mode only configures notification descriptors in 8A7A and ATVV services.");
        Console.WriteLine("Voice capture explicitly writes only documented ATVV GET_CAPS/MIC_OPEN/MIC_CLOSE commands.");
        Console.WriteLine("Voice live uses the same commands and also writes decoded PCM to an existing playback endpoint.");
        Console.WriteLine("Typeless live temporarily suppresses physical F5 and injects RightCtrl+RightShift on HTT start/stop.");
        Console.WriteLine("Menu Translate live reserves the physical Menu key while running; backtick stays available.");
    }
}

internal sealed class NotificationCandidate
{
    public NotificationCandidate(
        Guid serviceUuid,
        GattCharacteristic characteristic,
        GattClientCharacteristicConfigurationDescriptorValue subscriptionMode)
    {
        ServiceUuid = serviceUuid;
        Characteristic = characteristic;
        SubscriptionMode = subscriptionMode;
    }

    public Guid ServiceUuid { get; }
    public GattCharacteristic Characteristic { get; }
    public GattClientCharacteristicConfigurationDescriptorValue SubscriptionMode { get; }

    public void OnValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        CryptographicBuffer.CopyToByteArray(args.CharacteristicValue, out byte[] bytes);
        Console.WriteLine(
            $"{DateTimeOffset.Now:HH:mm:ss.fff} NOTIFY service={ServiceUuid:D} " +
            $"char={sender.Uuid:D} bytes={Convert.ToHexString(bytes)}");
    }
}
