using System.Runtime.InteropServices;

namespace MiVibe.Remote.GattProbe;

internal static class PhysicalMicrophoneKeyState
{
    // The project's installed scan-code map converts the remote microphone's
    // original F5 event to F13, which Typeless ignores as a shortcut.
    private const int VkF13 = 0x7C;

    public static bool IsDown()
    {
        return (GetAsyncKeyState(VkF13) & 0x8000) != 0;
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);
}
