namespace MiVibe.Remote.GattProbe;

internal static class PcmAudioProcessor
{
    public static void ApplyGainAndLimit(Span<short> samples, double gainDb)
    {
        if (Math.Abs(gainDb) < 0.001)
        {
            return;
        }

        double multiplier = Math.Pow(10.0, gainDb / 20.0);
        for (int index = 0; index < samples.Length; index++)
        {
            double amplified = samples[index] * multiplier;
            samples[index] = (short)Math.Clamp(
                Math.Round(amplified),
                short.MinValue,
                short.MaxValue);
        }
    }
}
