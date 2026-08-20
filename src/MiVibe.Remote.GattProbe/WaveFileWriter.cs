using System.Text;

namespace MiVibe.Remote.GattProbe;

internal static class WaveFileWriter
{
    public static void WriteMono16(string path, ReadOnlySpan<short> samples, int sampleRate)
    {
        const short channels = 1;
        const short bitsPerSample = 16;
        short blockAlign = channels * (bitsPerSample / 8);
        int byteRate = sampleRate * blockAlign;
        int dataLength = samples.Length * sizeof(short);

        using FileStream stream = File.Create(path);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: false);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataLength);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write(blockAlign);
        writer.Write(bitsPerSample);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataLength);

        foreach (short sample in samples)
        {
            writer.Write(sample);
        }
    }
}
