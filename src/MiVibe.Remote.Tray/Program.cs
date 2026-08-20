namespace MiVibe.Remote.Tray;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        int? smokeTestSeconds = ParseSmokeTestSeconds(args);
        bool reconnectSmokeTest = smokeTestSeconds is not null &&
            args.Contains("--reconnect-smoke-test", StringComparer.OrdinalIgnoreCase);
        Application.Run(new TrayApplicationContext(smokeTestSeconds, reconnectSmokeTest));
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
