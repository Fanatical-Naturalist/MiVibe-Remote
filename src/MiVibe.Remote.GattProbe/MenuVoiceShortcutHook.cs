namespace MiVibe.Remote.GattProbe;

// Preserve the original diagnostic and standalone voice-bridge entry points.
internal sealed class MenuVoiceShortcutHook : IDisposable
{
    private readonly RemoteKeyActions actions = new();

    public void Dispose() => actions.Dispose();
}
