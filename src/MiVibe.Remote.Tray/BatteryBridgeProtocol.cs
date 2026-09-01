using System.Globalization;

namespace MiVibe.Remote.Tray;

internal enum BatteryBridgeEventKind
{
    Level,
    Unknown,
    Stale
}

internal readonly record struct BatteryBridgeEvent(
    BatteryBridgeEventKind Kind,
    int? Level,
    string Source);

internal static class BatteryBridgeProtocol
{
    private const string Prefix = "MIVIBE_EVENT|1|BATTERY|";

    public static bool TryParse(string line, out BatteryBridgeEvent batteryEvent)
    {
        batteryEvent = default;
        if (!line.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        string[] fields = line.Split('|', StringSplitOptions.None);
        if (fields.Length != 6 || string.IsNullOrWhiteSpace(fields[5]))
        {
            return false;
        }

        switch (fields[3])
        {
            case "LEVEL" when
                int.TryParse(
                    fields[4],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int level) &&
                level is >= 0 and <= 100:
                batteryEvent = new BatteryBridgeEvent(
                    BatteryBridgeEventKind.Level,
                    level,
                    fields[5]);
                return true;

            case "UNKNOWN" when fields[4] == "-":
                batteryEvent = new BatteryBridgeEvent(
                    BatteryBridgeEventKind.Unknown,
                    Level: null,
                    fields[5]);
                return true;

            case "STALE" when fields[4] == "-":
                batteryEvent = new BatteryBridgeEvent(
                    BatteryBridgeEventKind.Stale,
                    Level: null,
                    fields[5]);
                return true;

            default:
                return false;
        }
    }
}
