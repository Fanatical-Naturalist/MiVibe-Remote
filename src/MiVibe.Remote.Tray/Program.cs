namespace MiVibe.Remote.Tray;

internal static class Program
{
    private const string SingleInstanceMutexName =
        "Local\\MiVibe.Remote.Tray-2717-32B8";
    private const string ShowWindowEventName =
        "Local\\MiVibe.Remote.ShowWindow-2717-32B8";

    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        int checksIndex = Array.IndexOf(args, "--verify-ui-state");
        if (checksIndex >= 0 && checksIndex + 1 < args.Length)
        {
            UiPreviewRenderer.VerifyPauseControls(args[checksIndex + 1]);
            return;
        }
        int previewIndex = Array.IndexOf(args, "--ui-preview");
        if (previewIndex >= 0 && previewIndex + 1 < args.Length)
        {
            UiPreviewRenderer.Render(args[previewIndex + 1]);
            return;
        }
        bool showStatusWindow = args.Contains(
            "--show-status-window", StringComparer.OrdinalIgnoreCase);
        using var showWindowEvent = new EventWaitHandle(
            false, EventResetMode.AutoReset, ShowWindowEventName);
        using var instanceMutex = new Mutex(
            initiallyOwned: true,
            SingleInstanceMutexName,
            out bool createdNew);
        if (!createdNew)
        {
            if (showStatusWindow)
            {
                showWindowEvent.Set();
            }
            return;
        }

        int? smokeTestSeconds = ParseSmokeTestSeconds(args);
        bool reconnectSmokeTest = smokeTestSeconds is not null &&
            args.Contains("--reconnect-smoke-test", StringComparer.OrdinalIgnoreCase);
        using var context = new TrayApplicationContext(
            smokeTestSeconds,
            reconnectSmokeTest,
            showStatusWindow);
        RegisteredWaitHandle showWindowWait = ThreadPool.RegisterWaitForSingleObject(
            showWindowEvent, (_, _) => context.ShowControlCenter(),
            null, Timeout.Infinite, executeOnlyOnce: false);
        try
        {
            Application.Run(context);
        }
        finally
        {
            showWindowWait.Unregister(null);
        }
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
