namespace MiVibe.Remote.Tray;

internal static class Program
{
    private const string SingleInstanceMutexName =
        "Local\\MiVibe.Remote.Tray-2717-32B8";

    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        using var instanceMutex = new Mutex(
            initiallyOwned: true,
            SingleInstanceMutexName,
            out bool createdNew);
        if (!createdNew)
        {
            return;
        }

        int? smokeTestSeconds = ParseSmokeTestSeconds(args);
        bool reconnectSmokeTest = smokeTestSeconds is not null &&
            args.Contains("--reconnect-smoke-test", StringComparer.OrdinalIgnoreCase);
        bool showStatusWindow = args.Contains(
            "--show-status-window",
            StringComparer.OrdinalIgnoreCase);
        Application.Run(new TrayApplicationContext(
            smokeTestSeconds,
            reconnectSmokeTest,
            showStatusWindow));
    }

    private static int? ParseSmokeTestSeconds(string[] args)
    {
        int index = Array.FindIndex(
            args,
            item => item.Equals("--smoke-test", StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return null;
        }

        return index + 1 < args.Length &&
               int.TryParse(args[index + 1], out int seconds) &&
               seconds is >= 5 and <= 30
            ? seconds
            : 10;
    }
}
