using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Security.Cryptography;

namespace MiVibe.Remote.GattProbe;

internal static class AtvvVoiceCapture
{
    private static readonly Guid ServiceUuid =
        Guid.Parse("AB5E0001-5A21-4F05-BC7D-AF01F617B664");
    private static readonly Guid TxUuid =
        Guid.Parse("AB5E0002-5A21-4F05-BC7D-AF01F617B664");
    private static readonly Guid AudioUuid =
        Guid.Parse("AB5E0003-5A21-4F05-BC7D-AF01F617B664");
    private static readonly Guid ControlUuid =
        Guid.Parse("AB5E0004-5A21-4F05-BC7D-AF01F617B664");

    private static readonly byte[] GetCapabilitiesCommand = [0x0A, 0x01, 0x00, 0x00, 0x03, 0x00];
    private static readonly byte[] OpenMicrophoneCommand = [0x0C, 0x00];

    public static async Task<int> RunAsync(
        BluetoothLEDevice device,
        IReadOnlyList<GattDeviceService> services,
        int captureSeconds,
        string outputPath,
        double gainDb,
        string? renderDeviceName,
        bool controlTypeless,
        bool openCodexVoice,
        bool controlCodexVoiceWithMenu)
    {
        GattDeviceService? service = services.FirstOrDefault(item => item.Uuid == ServiceUuid);
        if (service is null)
        {
            Console.Error.WriteLine("ATVV service was not found.");
            return 20;
        }

        GattCharacteristicsResult characteristicsResult =
            await service.GetCharacteristicsAsync(BluetoothCacheMode.Uncached);
        if (characteristicsResult.Status != GattCommunicationStatus.Success)
        {
            Console.Error.WriteLine(
                $"ATVV characteristics unavailable: {characteristicsResult.Status}.");
            return 21;
        }

        GattCharacteristic? tx = characteristicsResult.Characteristics.FirstOrDefault(item => item.Uuid == TxUuid);
        GattCharacteristic? audio = characteristicsResult.Characteristics.FirstOrDefault(item => item.Uuid == AudioUuid);
        GattCharacteristic? control = characteristicsResult.Characteristics.FirstOrDefault(item => item.Uuid == ControlUuid);
        if (tx is null || audio is null || control is null)
        {
            Console.Error.WriteLine("ATVV TX, AUDIO, or CONTROL characteristic is missing.");
            return 22;
        }

        var session = new CaptureSession(device, service.Session, tx, audio, control);
        return await session.RunAsync(
            captureSeconds,
            outputPath,
            gainDb,
            renderDeviceName,
            controlTypeless,
            openCodexVoice,
            controlCodexVoiceWithMenu,
            retainAudio: true,
            CancellationToken.None);
    }

    public static async Task<int> RunResidentAsync(
        BluetoothLEDevice device,
        IReadOnlyList<GattDeviceService> services,
        double gainDb,
        string renderDeviceName,
        CancellationToken cancellationToken)
    {
        GattDeviceService? service = services.FirstOrDefault(item => item.Uuid == ServiceUuid);
        if (service is null)
        {
            Console.Error.WriteLine("ATVV service was not found.");
            return 20;
        }

        GattCharacteristicsResult characteristicsResult =
            await service.GetCharacteristicsAsync(BluetoothCacheMode.Uncached);
        if (characteristicsResult.Status != GattCommunicationStatus.Success)
        {
            Console.Error.WriteLine(
                $"ATVV characteristics unavailable: {characteristicsResult.Status}.");
            return 21;
        }

        GattCharacteristic? tx = characteristicsResult.Characteristics.FirstOrDefault(item => item.Uuid == TxUuid);
        GattCharacteristic? audio = characteristicsResult.Characteristics.FirstOrDefault(item => item.Uuid == AudioUuid);
        GattCharacteristic? control = characteristicsResult.Characteristics.FirstOrDefault(item => item.Uuid == ControlUuid);
        if (tx is null || audio is null || control is null)
        {
            Console.Error.WriteLine("ATVV TX, AUDIO, or CONTROL characteristic is missing.");
            return 22;
        }

        var session = new CaptureSession(device, service.Session, tx, audio, control);
        return await session.RunAsync(
            captureSeconds: null,
            outputPath: null,
            gainDb,
            renderDeviceName,
            shouldControlTypeless: false,
            shouldOpenCodexVoice: false,
            shouldControlCodexVoiceWithMenu: true,
            retainAudio: false,
            cancellationToken);
    }

    private sealed class CaptureSession(
        BluetoothLEDevice device,
        GattSession gattSession,
        GattCharacteristic tx,
        GattCharacteristic audio,
        GattCharacteristic control)
    {
        private readonly TaskCompletionSource<byte[]> capabilitiesResponse =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<AudioStartInfo> audioStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<byte[]> audioStopped =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<GattSessionStatus> sessionEnded =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ConcurrentQueue<byte[]> audioPackets = new();
        private readonly ConcurrentQueue<short[]> pcmChunks = new();
        private Exception? protocolError;
        private byte streamId;
        private int audioPacketCount;
        private int discardedAudioPacketCount;
        private int physicalSegmentCount;
        private int activePhysicalSegmentNumber;
        private long physicalSegmentStartedTimestamp;
        private long adpcmByteCount;
        private long pcmSampleCount;
        private bool closeSent;
        private BluetoothLEPreferredConnectionParametersRequest? throughputRequest;
        private readonly ImaAdpcmDecoder.StreamDecoder liveDecoder = new();
        private readonly object liveDecodeLock = new();
        private bool captureArmed;
        private bool physicalSegmentActive;
        private CableAudioRenderer? liveRenderer;
        private double liveGainDb;
        private F5SuppressionHook? f5SuppressionHook;
        private MenuVoiceShortcutHook? menuVoiceShortcutHook;
        private bool typelessActive;
        private bool controlTypeless;
        private bool retainSessionAudio;

        public async Task<int> RunAsync(
            int? captureSeconds,
            string? outputPath,
            double gainDb,
            string? renderDeviceName,
            bool shouldControlTypeless,
            bool shouldOpenCodexVoice,
            bool shouldControlCodexVoiceWithMenu,
            bool retainAudio,
            CancellationToken cancellationToken)
        {
            bool controlSubscribed = false;
            bool audioSubscribed = false;

            control.ValueChanged += OnControlChanged;
            audio.ValueChanged += OnAudioChanged;
            gattSession.MaxPduSizeChanged += OnMaxPduSizeChanged;
            gattSession.SessionStatusChanged += OnSessionStatusChanged;

            try
            {
                controlTypeless = shouldControlTypeless;
                retainSessionAudio = retainAudio;
                if (controlTypeless)
                {
                    f5SuppressionHook = new F5SuppressionHook();
                    Console.WriteLine(
                        "Typeless push-to-talk armed: remote F5 is suppressed; " +
                        "RightCtrl+RightShift will be toggled from ATVV HTT start/stop.");
                }

                if (gattSession.CanMaintainConnection)
                {
                    gattSession.MaintainConnection = true;
                }

                Console.WriteLine(
                    $"GATT session: status={gattSession.SessionStatus} " +
                    $"canMaintain={gattSession.CanMaintainConnection} " +
                    $"maintain={gattSession.MaintainConnection} maxPduSize={gattSession.MaxPduSize}");

                try
                {
                    throughputRequest = device.RequestPreferredConnectionParameters(
                        BluetoothLEPreferredConnectionParameters.ThroughputOptimized);
                    Console.WriteLine("BLE connection preference: ThroughputOptimized requested for this capture.");
                }
                catch (Exception exception)
                {
                    Console.WriteLine(
                        $"BLE throughput preference was unavailable; continuing: {exception.Message}");
                }

                controlSubscribed = await SubscribeAsync(control, "CONTROL");
                audioSubscribed = await SubscribeAsync(audio, "AUDIO");
                if (!controlSubscribed || !audioSubscribed)
                {
                    Console.Error.WriteLine("ATVV notification subscription failed.");
                    return 23;
                }

                Console.WriteLine($"ATVV TX {Convert.ToHexString(GetCapabilitiesCommand)} GET_CAPS");
                if (!await WriteCommandAsync(GetCapabilitiesCommand))
                {
                    return 24;
                }

                byte[] capabilities = await WaitWithTimeoutAsync(
                    capabilitiesResponse.Task,
                    TimeSpan.FromSeconds(5),
                    "CAPS_RESP");
                CapabilitiesInfo capabilityInfo = ParseCapabilities(capabilities);
                PrintCapabilities(capabilityInfo);
                if ((capabilityInfo.Version >> 8) != 1)
                {
                    Console.Error.WriteLine(
                        $"This first probe supports ATVV 1.x only; remote reported " +
                        $"{capabilityInfo.Version >> 8}.{capabilityInfo.Version & 0xFF}. " +
                        "No MIC_OPEN command was sent.");
                    return 28;
                }

                Console.WriteLine($"ATVV TX {Convert.ToHexString(OpenMicrophoneCommand)} MIC_OPEN playback-mode");
                if (!await WriteCommandAsync(OpenMicrophoneCommand))
                {
                    return 25;
                }

                AudioStartInfo start = await WaitWithTimeoutAsync(
                    audioStarted.Task,
                    TimeSpan.FromSeconds(5),
                    "AUDIO_START");
                streamId = start.StreamId;
                int sampleRate = start.Codec switch
                {
                    0x01 => 8_000,
                    0x02 => 16_000,
                    _ => throw new InvalidDataException($"Unsupported ATVV codec 0x{start.Codec:X2}.")
                };

                Console.WriteLine(
                    $"ATVV audio started: reason=0x{start.Reason:X2} codec=0x{start.Codec:X2} " +
                    $"sampleRate={sampleRate} streamId=0x{streamId:X2}");
                liveGainDb = gainDb;
                if (renderDeviceName is not null)
                {
                    liveRenderer = new CableAudioRenderer(renderDeviceName, sampleRate);
                    Console.WriteLine(
                        "Live route armed. Hold the physical microphone button while speaking; " +
                        "the remote firmware requires this privacy gate.");
                }
                lock (liveDecodeLock)
                {
                    captureArmed = true;
                    physicalSegmentActive = false;
                }
                Console.WriteLine(
                    captureSeconds is null
                        ? "Resident bridge armed. Start a fresh physical microphone press now; " +
                          "press Ctrl+C to exit safely."
                        : $"Capture armed for {captureSeconds} seconds. " +
                          "Start a fresh physical microphone press now; any pre-arm audio was discarded.");

                if (shouldControlCodexVoiceWithMenu)
                {
                    menuVoiceShortcutHook = new MenuVoiceShortcutHook();
                    Console.WriteLine(
                        "Menu-controlled Codex Voice armed. Tap the remote Menu key to open or " +
                        "close Voice, then hold the remote microphone button while speaking. " +
                        "Tap Home for one Delete. The computer backtick key remains available " +
                        "for coding; the physical computer Home key is reserved while this bridge runs.");
                }

                if (shouldOpenCodexVoice)
                {
                    Console.WriteLine(
                        "Codex Voice mode is ready. Focus Codex now; Ctrl+Alt+* will be " +
                        "sent in 5 seconds...");
                    await Task.Delay(TimeSpan.FromSeconds(5));
                    TypelessShortcut.ToggleCodexVoice();
                    Console.WriteLine(
                        "Codex Voice shortcut sent. Hold the remote microphone button while speaking.");
                }

                await WaitForCaptureAsync(captureSeconds, cancellationToken);

                byte[] closeCommand = [0x0D, streamId];
                Console.WriteLine($"ATVV TX {Convert.ToHexString(closeCommand)} MIC_CLOSE");
                closeSent = await WriteCommandAsync(closeCommand);

                try
                {
                    await WaitWithTimeoutAsync(
                        audioStopped.Task,
                        TimeSpan.FromSeconds(2),
                        "AUDIO_STOP");
                }
                catch (TimeoutException)
                {
                    Console.WriteLine("AUDIO_STOP was not observed before cleanup; continuing safely.");
                }

                if (protocolError is not null)
                {
                    throw protocolError;
                }

                if (!retainAudio)
                {
                    Console.WriteLine(
                        $"Resident session complete: packets={audioPacketCount} " +
                        $"adpcmBytes={adpcmByteCount} pcmSamples={pcmSampleCount} " +
                        $"segments={physicalSegmentCount} " +
                        $"discardedPreArmOrInactive={discardedAudioPacketCount} " +
                        $"gainDb={gainDb:+0.##;-0.##;0}");
                    return 0;
                }

                byte[] adpcm = FlattenPackets(audioPackets);
                if (adpcm.Length == 0)
                {
                    Console.Error.WriteLine("No ATVV audio packets were received.");
                    return 26;
                }

                short[] pcm = FlattenPcmChunks(pcmChunks);
                string fullOutputPath = Path.GetFullPath(outputPath!);
                string? directory = Path.GetDirectoryName(fullOutputPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string rawPath = Path.ChangeExtension(fullOutputPath, ".adpcm");
                await File.WriteAllBytesAsync(rawPath, adpcm);
                WaveFileWriter.WriteMono16(fullOutputPath, pcm, sampleRate);

                Console.WriteLine(
                    $"Capture complete: packets={audioPackets.Count} adpcmBytes={adpcm.Length} " +
                    $"pcmSamples={pcm.Length} segments={physicalSegmentCount} " +
                    $"discardedPreArmOrInactive={discardedAudioPacketCount} " +
                    $"gainDb={gainDb:+0.##;-0.##;0}");
                Console.WriteLine($"WAV  {fullOutputPath}");
                Console.WriteLine($"RAW  {rawPath}");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"ATVV capture failed: {exception.Message}");
                return 27;
            }
            finally
            {
                lock (liveDecodeLock)
                {
                    captureArmed = false;
                    physicalSegmentActive = false;
                }

                if (!closeSent && (streamId != 0 || audioStarted.Task.IsCompletedSuccessfully))
                {
                    await WriteCommandAsync([0x0D, streamId]);
                }

                if (audioSubscribed)
                {
                    await UnsubscribeAsync(audio, "AUDIO");
                }

                if (controlSubscribed)
                {
                    await UnsubscribeAsync(control, "CONTROL");
                }

                audio.ValueChanged -= OnAudioChanged;
                control.ValueChanged -= OnControlChanged;
                gattSession.MaxPduSizeChanged -= OnMaxPduSizeChanged;
                gattSession.SessionStatusChanged -= OnSessionStatusChanged;
                if (gattSession.CanMaintainConnection)
                {
                    gattSession.MaintainConnection = false;
                }

                throughputRequest?.Dispose();
                menuVoiceShortcutHook?.Dispose();
                liveRenderer?.Dispose();
                EnsureTypelessStopped();
                f5SuppressionHook?.Dispose();
            }
        }

        private async Task WaitForCaptureAsync(
            int? captureSeconds,
            CancellationToken cancellationToken)
        {
            int? remaining = captureSeconds;
            while (remaining is null || remaining > 0)
            {
                int interval = remaining is null ? 8 : Math.Min(8, remaining.Value);
                try
                {
                    Task delay = Task.Delay(TimeSpan.FromSeconds(interval), cancellationToken);
                    Task completed = await Task.WhenAny(delay, sessionEnded.Task);
                    if (completed == sessionEnded.Task && !cancellationToken.IsCancellationRequested)
                    {
                        GattSessionStatus status = await sessionEnded.Task;
                        throw new IOException($"GATT session ended with status {status}.");
                    }

                    await delay;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                if (remaining is not null)
                {
                    remaining -= interval;
                }

                if (remaining is null || remaining > 0)
                {
                    byte[] extendCommand = [0x0E, streamId];
                    Console.WriteLine($"ATVV TX {Convert.ToHexString(extendCommand)} MIC_EXTEND");
                    if (!await WriteCommandAsync(extendCommand))
                    {
                        throw new IOException(
                            "ATVV keepalive failed; the BLE session will be reopened by the tray host.");
                    }
                }
            }
        }

        private void OnSessionStatusChanged(
            GattSession sender,
            GattSessionStatusChangedEventArgs args)
        {
            Console.WriteLine($"GATT session status changed: {args.Status}.");
            if (args.Status != GattSessionStatus.Active)
            {
                sessionEnded.TrySetResult(args.Status);
            }
        }

        private async Task<bool> WriteCommandAsync(byte[] bytes)
        {
            GattWriteResult result = await tx.WriteValueWithResultAsync(
                bytes.AsBuffer(),
                GattWriteOption.WriteWithoutResponse);
            if (result.Status == GattCommunicationStatus.Success)
            {
                return true;
            }

            Console.Error.WriteLine(
                $"ATVV write failed: status={result.Status} " +
                $"protocolError={result.ProtocolError?.ToString() ?? "none"}");
            return false;
        }

        private void OnControlChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            CryptographicBuffer.CopyToByteArray(args.CharacteristicValue, out byte[] bytes);
            Console.WriteLine(
                $"{DateTimeOffset.Now:HH:mm:ss.fff} ATVV CONTROL {Convert.ToHexString(bytes)}");
            if (bytes.Length == 0)
            {
                return;
            }

            switch (bytes[0])
            {
                case 0x00 when bytes.Length >= 2 && bytes[1] == 0x02:
                    EndPhysicalSegment(bytes[1]);
                    SetTypelessActive(false);
                    break;
                case 0x00:
                    EndPhysicalSegment(bytes.Length >= 2 ? bytes[1] : null);
                    SetTypelessActive(false);
                    audioStopped.TrySetResult(bytes);
                    break;
                case 0x04 when bytes.Length >= 4:
                    streamId = bytes[3];
                    if (bytes[1] == 0x03)
                    {
                        BeginPhysicalSegment();
                        SetTypelessActive(true);
                    }
                    audioStarted.TrySetResult(new AudioStartInfo(bytes[1], bytes[2], bytes[3]));
                    break;
                case 0x0B:
                    capabilitiesResponse.TrySetResult(bytes);
                    break;
                case 0x0C when bytes.Length >= 3:
                    ushort error = (ushort)((bytes[1] << 8) | bytes[2]);
                    protocolError = new InvalidOperationException(
                        $"Remote rejected MIC_OPEN with ATVV error 0x{error:X4}.");
                    audioStarted.TrySetException(protocolError);
                    break;
            }
        }

        private void SetTypelessActive(bool active)
        {
            if (!controlTypeless || typelessActive == active)
            {
                return;
            }

            try
            {
                TypelessShortcut.ToggleDictation();
                typelessActive = active;
                Console.WriteLine(
                    $"Typeless dictation {(active ? "started" : "stopped")} " +
                    "with RightCtrl+RightShift.");
            }
            catch (Exception exception)
            {
                protocolError ??= exception;
                Console.Error.WriteLine(
                    $"Typeless shortcut injection failed: {exception.Message}");
            }
        }

        private void EnsureTypelessStopped()
        {
            if (typelessActive)
            {
                SetTypelessActive(false);
            }
        }

        private void OnAudioChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            CryptographicBuffer.CopyToByteArray(args.CharacteristicValue, out byte[] bytes);
            if (bytes.Length == 0)
            {
                return;
            }

            short[] pcm;
            lock (liveDecodeLock)
            {
                if (!captureArmed || !physicalSegmentActive)
                {
                    int discarded = Interlocked.Increment(ref discardedAudioPacketCount);
                    if (discarded == 1)
                    {
                        Console.WriteLine(
                            $"{DateTimeOffset.Now:HH:mm:ss.fff} ATVV AUDIO discarded before " +
                            "a fresh armed microphone segment.");
                    }
                    return;
                }

                if (retainSessionAudio)
                {
                    audioPackets.Enqueue(bytes);
                }
                pcm = liveDecoder.DecodeChunk(bytes);
                PcmAudioProcessor.ApplyGainAndLimit(pcm, liveGainDb);
                if (retainSessionAudio)
                {
                    pcmChunks.Enqueue(pcm);
                }
                liveRenderer?.Write(pcm);
            }

            Interlocked.Add(ref adpcmByteCount, bytes.Length);
            Interlocked.Add(ref pcmSampleCount, pcm.Length);
            int count = Interlocked.Increment(ref audioPacketCount);
            if (count <= 5)
            {
                int previewLength = Math.Min(bytes.Length, 16);
                Console.WriteLine(
                    $"{DateTimeOffset.Now:HH:mm:ss.fff} ATVV AUDIO packet={count} " +
                    $"length={bytes.Length} preview={Convert.ToHexString(bytes, 0, previewLength)}");
            }
        }

        private void BeginPhysicalSegment()
        {
            int segment;
            lock (liveDecodeLock)
            {
                if (!captureArmed)
                {
                    return;
                }

                liveDecoder.Reset();
                segment = Interlocked.Increment(ref physicalSegmentCount);
                activePhysicalSegmentNumber = segment;
                physicalSegmentStartedTimestamp = Stopwatch.GetTimestamp();
                physicalSegmentActive = true;
            }

            Console.WriteLine($"ATVV physical microphone segment {segment} started; decoder reset.");
        }

        private void EndPhysicalSegment(byte? stopReason)
        {
            int segment;
            long startedTimestamp;
            lock (liveDecodeLock)
            {
                if (!physicalSegmentActive)
                {
                    return;
                }

                physicalSegmentActive = false;
                segment = activePhysicalSegmentNumber;
                startedTimestamp = physicalSegmentStartedTimestamp;
            }

            TimeSpan elapsed = Stopwatch.GetElapsedTime(startedTimestamp);
            bool microphoneKeyDown = PhysicalMicrophoneKeyState.IsDown();
            string reason = stopReason is null ? "unknown" : $"0x{stopReason.Value:X2}";
            Console.WriteLine(
                $"ATVV physical microphone segment {segment} ended after " +
                $"{elapsed.TotalSeconds:F3}s; stopReason={reason}; " +
                $"mappedF13Down={microphoneKeyDown}.");
        }

        private static void OnMaxPduSizeChanged(GattSession sender, object args)
        {
            Console.WriteLine(
                $"{DateTimeOffset.Now:HH:mm:ss.fff} GATT maxPduSize changed to {sender.MaxPduSize}");
        }

        private static CapabilitiesInfo ParseCapabilities(byte[] bytes)
        {
            if (bytes.Length < 7 || bytes[0] != 0x0B)
            {
                throw new InvalidDataException(
                    $"CAPS_RESP has unexpected length/format: {Convert.ToHexString(bytes)}");
            }

            ushort version = (ushort)((bytes[1] << 8) | bytes[2]);
            ushort frameSize = (ushort)((bytes[5] << 8) | bytes[6]);
            return new CapabilitiesInfo(version, bytes[3], bytes[4], frameSize);
        }

        private static void PrintCapabilities(CapabilitiesInfo info)
        {
            Console.WriteLine(
                $"ATVV capabilities: version={(info.Version >> 8)}.{(info.Version & 0xFF)} " +
                $"codecs=0x{info.Codecs:X2} interaction=0x{info.InteractionModel:X2} " +
                $"frameSize={info.FrameSize}");
        }

        private static async Task<bool> SubscribeAsync(GattCharacteristic characteristic, string label)
        {
            GattCommunicationStatus status =
                await characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                    GattClientCharacteristicConfigurationDescriptorValue.Notify);
            GattReadClientCharacteristicConfigurationDescriptorResult readResult =
                await characteristic.ReadClientCharacteristicConfigurationDescriptorAsync();
            Console.WriteLine(
                $"ATVV subscribe {label}: write={status} read={readResult.Status} " +
                $"value={readResult.ClientCharacteristicConfigurationDescriptor}");
            return status == GattCommunicationStatus.Success;
        }

        private static async Task UnsubscribeAsync(GattCharacteristic characteristic, string label)
        {
            try
            {
                GattCommunicationStatus status =
                    await characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                        GattClientCharacteristicConfigurationDescriptorValue.None);
                Console.WriteLine($"ATVV unsubscribe {label}: {status}");
            }
            catch (Exception exception)
            {
                Console.WriteLine($"ATVV unsubscribe {label} failed: {exception.Message}");
            }
        }

        private static async Task<T> WaitWithTimeoutAsync<T>(
            Task<T> task,
            TimeSpan timeout,
            string operation)
        {
            using var cancellation = new CancellationTokenSource(timeout);
            try
            {
                return await task.WaitAsync(cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException($"Timed out waiting for {operation}.");
            }
        }

        private static byte[] FlattenPackets(ConcurrentQueue<byte[]> packets)
        {
            int totalLength = packets.Sum(packet => packet.Length);
            var result = new byte[totalLength];
            int offset = 0;
            foreach (byte[] packet in packets)
            {
                Buffer.BlockCopy(packet, 0, result, offset, packet.Length);
                offset += packet.Length;
            }

            return result;
        }

        private static short[] FlattenPcmChunks(ConcurrentQueue<short[]> chunks)
        {
            int totalLength = chunks.Sum(chunk => chunk.Length);
            var result = new short[totalLength];
            int offset = 0;
            foreach (short[] chunk in chunks)
            {
                Array.Copy(chunk, 0, result, offset, chunk.Length);
                offset += chunk.Length;
            }

            return result;
        }
    }

    private sealed record AudioStartInfo(byte Reason, byte Codec, byte StreamId);
    private sealed record CapabilitiesInfo(
        ushort Version,
        byte Codecs,
        byte InteractionModel,
        ushort FrameSize);
}
