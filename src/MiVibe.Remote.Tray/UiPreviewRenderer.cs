using System.Drawing;
using System.Drawing.Imaging;

namespace MiVibe.Remote.Tray;

/// <summary>Renders illustrative UI states without starting hardware or input hooks.</summary>
internal static class UiPreviewRenderer
{
    public static void VerifyPauseControls(string outputPath)
    {
        using var window = new StatusWindow();
        var cases = new[]
        {
            ("paused", ConnectionPhase.Paused, KeyBridgePhase.Disabled, false, true, false, false, false),
            ("voice-error", ConnectionPhase.Error, KeyBridgePhase.Disabled, false, false, false, true, false),
            ("paused-error", ConnectionPhase.Error, KeyBridgePhase.Error, false, true, false, false, false),
            ("retry-stop", ConnectionPhase.Error, KeyBridgePhase.Error, true, true, false, true, false),
            ("busy", ConnectionPhase.Connected, KeyBridgePhase.Active, true, false, true, false, false),
            ("active", ConnectionPhase.Connected, KeyBridgePhase.Active, true, false, false, true, true)
        };
        var passed = new List<string>();
        foreach (var (name, connection, keys, running, paused, busy, toggle, calibrate) in cases)
        {
            window.ApplyState(TrayUiState.Initial with
            {
                Connection = connection, KeyBridge = keys, KeyBridgeRunning = running,
                OperationInProgress = busy
            }, isPaused: paused);
            string toggleName = running ? "停用增强按键" : "启用增强按键";
            Button toggleButton = Descendants(window).OfType<Button>().Single(button => button.Text == toggleName);
            Button calibrationButton = Descendants(window).OfType<Button>().Single(button => button.Text == "重新校准");
            if (toggleButton.Enabled != toggle || calibrationButton.Enabled != calibrate)
            {
                throw new InvalidOperationException($"UI state check failed: {name}");
            }
            passed.Add($"PASS {name}: toggle={toggle}, calibrate={calibrate}");
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        File.WriteAllLines(outputPath, passed);
        window.AllowCloseAndDispose();
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (Control descendant in Descendants(child)) yield return descendant;
        }
    }

    public static void Render(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        foreach (bool dark in new[] { true, false })
        {
            foreach (KeyBridgePhase phase in new[] { KeyBridgePhase.Active, KeyBridgePhase.Calibrating, KeyBridgePhase.Disabled })
            {
                using var window = new StatusWindow();
                window.SetPreviewTheme(dark);
                window.ApplyState(new TrayUiState(
                    "小米蓝牙语音遥控器", ConnectionPhase.Connected, "语音桥已就绪 · 界面示例",
                    null, 85, BatteryFreshness.Current, DateTimeOffset.Now, false, false,
                    phase, CalibrationStep: 1, LastKeyAction: "音量＋ · 上一个对话"));
                window.StartPosition = FormStartPosition.Manual;
                window.Location = new Point(-30_000, -30_000);
                window.Show();
                Application.DoEvents();
                window.PerformLayout();
                File.WriteAllLines(Path.Combine(outputDirectory, "layout-metrics.txt"), Describe(window));
                using var bitmap = new Bitmap(window.Width, window.Height);
                window.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                bitmap.Save(Path.Combine(outputDirectory, $"v03-{(dark ? "dark" : "light")}-{phase.ToString().ToLowerInvariant()}.png"), ImageFormat.Png);
                window.AllowCloseAndDispose();
            }
        }
    }

    private static IEnumerable<string> Describe(Control control, int depth = 0)
    {
        yield return $"{new string(' ', depth)}{control.GetType().Name} {control.Text} bounds={control.Bounds} dpi={control.DeviceDpi} fontHeight={control.Font.Height}";
        foreach (Control child in control.Controls)
        {
            foreach (string item in Describe(child, depth + 1)) yield return item;
        }
    }
}
