using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MiVibe.Remote.GattProbe;

internal sealed class CableAudioRenderer : IDisposable
{
    private readonly MMDeviceEnumerator enumerator = new();
    private readonly MMDevice device;
    private readonly BufferedWaveProvider buffer;
    private readonly WasapiOut output;
    private bool disposed;

    public CableAudioRenderer(string deviceName, int sampleRate)
    {
        device = enumerator
            .EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
            .FirstOrDefault(item => IsRequestedDevice(item.FriendlyName, deviceName))
            ?? throw new InvalidOperationException(
                $"Active playback device '{deviceName}' was not found. " +
                "Verify that VB-CABLE is installed and CABLE Input is enabled.");

        buffer = new BufferedWaveProvider(new WaveFormat(sampleRate, 16, 1))
        {
            BufferDuration = TimeSpan.FromSeconds(3),
            DiscardOnBufferOverflow = true,
            ReadFully = true
        };

        output = new WasapiOut(
            device,
            AudioClientShareMode.Shared,
            useEventSync: true,
            latency: 100);
        output.Init(buffer);
        output.Play();

        Console.WriteLine(
            $"VB-CABLE live output: device={device.FriendlyName} " +
            $"format={sampleRate}Hz/mono/16-bit mode=shared");
    }

    public string FriendlyName => device.FriendlyName;

    public void Write(ReadOnlySpan<short> samples)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(samples);
        buffer.AddSamples(bytes.ToArray(), 0, bytes.Length);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        output.Stop();
        output.Dispose();
        device.Dispose();
        enumerator.Dispose();
    }

    private static bool IsRequestedDevice(string friendlyName, string requestedName)
    {
        if (friendlyName.Equals(requestedName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return requestedName.Equals("CABLE Input", StringComparison.OrdinalIgnoreCase) &&
               friendlyName.StartsWith("CABLE Input ", StringComparison.OrdinalIgnoreCase) &&
               !friendlyName.Contains("16ch", StringComparison.OrdinalIgnoreCase);
    }
}
