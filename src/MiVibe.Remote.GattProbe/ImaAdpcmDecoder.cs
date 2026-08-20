namespace MiVibe.Remote.GattProbe;

internal static class ImaAdpcmDecoder
{
    private static readonly int[] StepTable =
    [
        7, 8, 9, 10, 11, 12, 13, 14, 16, 17, 19, 21, 23, 25, 28, 31,
        34, 37, 41, 45, 50, 55, 60, 66, 73, 80, 88, 97, 107, 118, 130, 143,
        157, 173, 190, 209, 230, 253, 279, 307, 337, 371, 408, 449, 494, 544, 598, 658,
        724, 796, 876, 963, 1060, 1166, 1282, 1411, 1552, 1707, 1878, 2066, 2272, 2499, 2749, 3024,
        3327, 3660, 4026, 4428, 4871, 5358, 5894, 6484, 7132, 7845, 8630, 9493, 10442, 11487,
        12635, 13899, 15289, 16818, 18500, 20350, 22385, 24623, 27086, 29794, 32767
    ];

    private static readonly int[] IndexTable =
    [
        -1, -1, -1, -1, 2, 4, 6, 8,
        -1, -1, -1, -1, 2, 4, 6, 8
    ];

    public static short[] Decode(ReadOnlySpan<byte> encoded)
    {
        return new StreamDecoder().DecodeChunk(encoded);
    }

    private static short DecodeNibble(int nibble, DecoderState state)
    {
        int step = StepTable[state.Index];
        int difference = step >> 3;
        if ((nibble & 1) != 0)
        {
            difference += step >> 2;
        }
        if ((nibble & 2) != 0)
        {
            difference += step >> 1;
        }
        if ((nibble & 4) != 0)
        {
            difference += step;
        }

        state.Predictor += (nibble & 8) != 0 ? -difference : difference;
        state.Predictor = Math.Clamp(state.Predictor, short.MinValue, short.MaxValue);
        state.Index = Math.Clamp(state.Index + IndexTable[nibble], 0, StepTable.Length - 1);
        return (short)state.Predictor;
    }

    private sealed class DecoderState
    {
        public int Predictor { get; set; }
        public int Index { get; set; }
    }

    public sealed class StreamDecoder
    {
        private readonly DecoderState state = new();

        public void Reset()
        {
            state.Predictor = 0;
            state.Index = 0;
        }

        public short[] DecodeChunk(ReadOnlySpan<byte> encoded)
        {
            var samples = new short[encoded.Length * 2];
            int outputIndex = 0;

            foreach (byte value in encoded)
            {
                samples[outputIndex++] = DecodeNibble((value >> 4) & 0x0F, state);
                samples[outputIndex++] = DecodeNibble(value & 0x0F, state);
            }

            return samples;
        }
    }
}
