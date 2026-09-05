namespace MiVibe.Remote.Tray;

internal enum ConnectionPhase
{
    Starting,
    Connecting,
    Connected,
    ReconnectWaiting,
    Paused,
    Stopping,
    Disconnected,
    Error
}

internal enum BatteryFreshness
{
    Unknown,
    Current,
    LastKnown
}

internal enum KeyBridgePhase
{
    Disabled,
    Starting,
    Calibrating,
    Active,
    Reconnecting,
    Error,
    Stopping
}

internal sealed record TrayUiState(
    string DeviceName,
    ConnectionPhase Connection,
    string Detail,
    int? ReconnectDelaySeconds,
    int? BatteryPercent,
    BatteryFreshness BatteryFreshness,
    DateTimeOffset? BatteryUpdatedAt,
    bool StartWithWindows,
    bool OperationInProgress,
    KeyBridgePhase KeyBridge = KeyBridgePhase.Disabled,
    string KeyBridgeDetail = "",
    int CalibrationStep = 0,
    string? LastKeyAction = null,
    bool KeyBridgeRunning = false)
{
    public static TrayUiState Initial { get; } = new(
        "小米蓝牙语音遥控器",
        ConnectionPhase.Starting,
        "正在准备语音桥…",
        ReconnectDelaySeconds: null,
        BatteryPercent: null,
        BatteryFreshness.Unknown,
        BatteryUpdatedAt: null,
        StartWithWindows: false,
        OperationInProgress: true);
}
