using System.Globalization;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Foundation;
using Windows.Security.Cryptography;
using Windows.Storage.Streams;

namespace MiVibe.Remote.GattProbe;

internal sealed class BatteryMonitor : IAsyncDisposable
{
    private static readonly Guid BatteryServiceUuid =
        Guid.Parse("0000180F-0000-1000-8000-00805F9B34FB");
    private static readonly Guid BatteryLevelUuid =
        Guid.Parse("00002A19-0000-1000-8000-00805F9B34FB");
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan UnsubscribeTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);

    private readonly IReadOnlyList<GattDeviceService> services;
    private readonly object stateLock = new();

    private CancellationTokenSource? lifetimeCancellation;
    private GattCharacteristic? batteryLevelCharacteristic;
    private Task? initializationTask;
    private Task? pollingTask;
    private int? lastValidLevel;
    private bool notificationSubscribed;
    private bool started;
    private bool disposed;
    private volatile bool stopping;

    public BatteryMonitor(IReadOnlyList<GattDeviceService> services)
    {
        this.services = services;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (started || disposed)
        {
            return Task.CompletedTask;
        }

        started = true;
        lifetimeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        initializationTask = InitializeAndReportAsync(lifetimeCancellation.Token);
        return Task.CompletedTask;
    }

    private async Task InitializeAndReportAsync(CancellationToken cancellationToken)
    {
        try
        {
            await InitializeAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The resident bridge is already shutting down.
        }
        catch (Exception exception)
        {
            Console.WriteLine(
                $"Battery monitor initialization failed; voice bridge will continue: " +
                $"{exception.GetType().Name}: {exception.Message}");
            PublishFailure("INITIALIZE_EXCEPTION");
            PublishMode("NONE");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        stopping = true;

        try
        {
            lifetimeCancellation?.Cancel();
        }
        catch (Exception exception)
        {
            Console.WriteLine(
                $"Battery cancellation cleanup failed; continuing shutdown: " +
                $"{exception.GetType().Name}: {exception.Message}");
        }

        if (initializationTask is not null)
        {
            try
            {
                await initializationTask;
            }
            catch (OperationCanceledException)
            {
                // Expected during resident shutdown.
            }
            catch (Exception exception)
            {
                Console.WriteLine(
                    $"Battery initialization cleanup failed; continuing shutdown: " +
                    $"{exception.GetType().Name}: {exception.Message}");
            }
        }

        if (pollingTask is not null)
        {
            try
            {
                await pollingTask;
            }
            catch (OperationCanceledException)
            {
                // Expected during resident shutdown.
            }
            catch (Exception exception)
            {
                Console.WriteLine(
                    $"Battery polling cleanup failed; continuing shutdown: " +
                    $"{exception.GetType().Name}: {exception.Message}");
            }
        }

        GattCharacteristic? characteristic = batteryLevelCharacteristic;
        if (characteristic is not null)
        {
            try
            {
                characteristic.ValueChanged -= OnBatteryLevelChanged;
            }
            catch (Exception exception)
            {
                Console.WriteLine(
                    $"Battery event-handler cleanup failed; continuing shutdown: " +
                    $"{exception.GetType().Name}: {exception.Message}");
            }
        }

        if (notificationSubscribed && characteristic is not null)
        {
            try
            {
                GattCommunicationStatus status = await AwaitOperationAsync(
                    characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                        GattClientCharacteristicConfigurationDescriptorValue.None),
                    UnsubscribeTimeout,
                    CancellationToken.None);
                Console.WriteLine($"Battery unsubscribe: status={status}.");
            }
            catch (Exception exception)
            {
                Console.WriteLine(
                    $"Battery unsubscribe failed; continuing shutdown: " +
                    $"{exception.GetType().Name}: {exception.Message}");
            }
        }

        notificationSubscribed = false;
        try
        {
            lifetimeCancellation?.Dispose();
        }
        catch (Exception exception)
        {
            Console.WriteLine(
                $"Battery cancellation source cleanup failed; continuing shutdown: " +
                $"{exception.GetType().Name}: {exception.Message}");
        }

        lifetimeCancellation = null;
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        GattDeviceService? batteryService = services.FirstOrDefault(
            service => service.Uuid == BatteryServiceUuid);
        if (batteryService is null)
        {
            PublishFailure("SERVICE_NOT_FOUND");
            PublishMode("NONE");
            return;
        }

        GattCharacteristicsResult characteristicsResult;
        try
        {
            characteristicsResult = await AwaitOperationAsync(
                batteryService.GetCharacteristicsAsync(BluetoothCacheMode.Uncached),
                OperationTimeout,
                cancellationToken);
        }
        catch (TimeoutException)
        {
            PublishFailure("CHARACTERISTICS_TIMEOUT");
            PublishMode("NONE");
            return;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            Console.WriteLine(
                $"Battery characteristic discovery failed; voice bridge will continue: " +
                $"{exception.GetType().Name}: {exception.Message}");
            PublishFailure("CHARACTERISTICS_EXCEPTION");
            PublishMode("NONE");
            return;
        }

        if (characteristicsResult.Status != GattCommunicationStatus.Success)
        {
            Console.WriteLine(
                $"Battery characteristics unavailable: status={characteristicsResult.Status} " +
                $"protocolError={characteristicsResult.ProtocolError?.ToString() ?? "none"}.");
            PublishFailure($"CHARACTERISTICS_{ToToken(characteristicsResult.Status)}");
            PublishMode("NONE");
            return;
        }

        batteryLevelCharacteristic = characteristicsResult.Characteristics.FirstOrDefault(
            characteristic => characteristic.Uuid == BatteryLevelUuid);
        if (batteryLevelCharacteristic is null)
        {
            PublishFailure("LEVEL_NOT_FOUND");
            PublishMode("NONE");
            return;
        }

        GattCharacteristicProperties properties =
            batteryLevelCharacteristic.CharacteristicProperties;
        bool canRead = properties.HasFlag(GattCharacteristicProperties.Read);
        bool canNotify = properties.HasFlag(GattCharacteristicProperties.Notify);

        if (canRead)
        {
            await ReadAndPublishAsync("INITIAL_READ", cancellationToken);
        }
        else
        {
            PublishFailure("READ_NOT_SUPPORTED");
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (canNotify && await TrySubscribeAsync(cancellationToken))
        {
            PublishMode("NOTIFY");
            return;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (canRead)
        {
            PublishMode("POLL_300S");
            pollingTask = PollAsync(cancellationToken);
        }
        else
        {
            PublishMode("NONE");
        }
    }

    private async Task<bool> TrySubscribeAsync(CancellationToken cancellationToken)
    {
        GattCharacteristic characteristic = batteryLevelCharacteristic!;
        characteristic.ValueChanged += OnBatteryLevelChanged;

        try
        {
            GattCommunicationStatus status = await AwaitOperationAsync(
                characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                    GattClientCharacteristicConfigurationDescriptorValue.Notify),
                OperationTimeout,
                cancellationToken);
            Console.WriteLine($"Battery subscribe: status={status}.");
            if (status == GattCommunicationStatus.Success)
            {
                notificationSubscribed = true;
                return true;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Resident shutdown owns the cancellation.
        }
        catch (TimeoutException)
        {
            Console.WriteLine("Battery notification subscription timed out; using polling fallback.");
        }
        catch (Exception exception)
        {
            Console.WriteLine(
                $"Battery notification subscription failed; using polling fallback: " +
                $"{exception.GetType().Name}: {exception.Message}");
        }

        characteristic.ValueChanged -= OnBatteryLevelChanged;
        return false;
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(PollInterval, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            await ReadAndPublishAsync("POLL", cancellationToken);
        }
    }

    private async Task ReadAndPublishAsync(
        string source,
        CancellationToken cancellationToken)
    {
        GattCharacteristic characteristic = batteryLevelCharacteristic!;
        try
        {
            GattReadResult result = await AwaitOperationAsync(
                characteristic.ReadValueAsync(BluetoothCacheMode.Uncached),
                OperationTimeout,
                cancellationToken);
            if (result.Status != GattCommunicationStatus.Success)
            {
                Console.WriteLine(
                    $"Battery read failed: status={result.Status} " +
                    $"protocolError={result.ProtocolError?.ToString() ?? "none"}.");
                PublishFailure($"READ_{ToToken(result.Status)}");
                return;
            }

            if (!TryParseLevel(result.Value, out int level))
            {
                PublishFailure("INVALID_PAYLOAD");
                return;
            }

            PublishLevel(level, source);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // No state event is needed while the resident bridge is stopping.
        }
        catch (TimeoutException)
        {
            PublishFailure("READ_TIMEOUT");
        }
        catch (Exception exception)
        {
            Console.WriteLine(
                $"Battery read failed; voice bridge will continue: " +
                $"{exception.GetType().Name}: {exception.Message}");
            PublishFailure("READ_EXCEPTION");
        }
    }

    private void OnBatteryLevelChanged(
        GattCharacteristic sender,
        GattValueChangedEventArgs args)
    {
        if (stopping)
        {
            return;
        }

        try
        {
            if (!TryParseLevel(args.CharacteristicValue, out int level))
            {
                PublishFailure("NOTIFY_INVALID_PAYLOAD");
                return;
            }

            PublishLevel(level, "NOTIFY");
        }
        catch (Exception exception)
        {
            Console.WriteLine(
                $"Battery notification was ignored: " +
                $"{exception.GetType().Name}: {exception.Message}");
            PublishFailure("NOTIFY_EXCEPTION");
        }
    }

    private void PublishLevel(int level, string source)
    {
        lock (stateLock)
        {
            lastValidLevel = level;
        }

        WriteEvent("LEVEL", level.ToString(CultureInfo.InvariantCulture), source);
    }

    private void PublishFailure(string reason)
    {
        bool hasLastValidLevel;
        lock (stateLock)
        {
            hasLastValidLevel = lastValidLevel.HasValue;
        }

        WriteEvent(hasLastValidLevel ? "STALE" : "UNKNOWN", "-", reason);
    }

    private static void PublishMode(string mode)
    {
        WriteEvent("MODE", "-", mode);
    }

    private static void WriteEvent(string kind, string value, string detail)
    {
        Console.WriteLine($"MIVIBE_EVENT|1|BATTERY|{kind}|{value}|{detail}");
    }

    private static bool TryParseLevel(IBuffer buffer, out int level)
    {
        level = 0;
        if (buffer is null || buffer.Length != 1)
        {
            return false;
        }

        CryptographicBuffer.CopyToByteArray(buffer, out byte[] bytes);
        if (bytes.Length != 1 || bytes[0] > 100)
        {
            return false;
        }

        level = bytes[0];
        return true;
    }

    private static string ToToken(GattCommunicationStatus status)
    {
        return status.ToString().ToUpperInvariant();
    }

    private static async Task<T> AwaitOperationAsync<T>(
        IAsyncOperation<T> operation,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var operationCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        operationCancellation.CancelAfter(timeout);

        try
        {
            return await operation.AsTask(operationCancellation.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException();
        }
    }
}
