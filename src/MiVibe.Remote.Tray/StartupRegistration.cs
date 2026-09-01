using Microsoft.Win32;
using System.Security;

namespace MiVibe.Remote.Tray;

internal static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "MiVibe Remote";

    public static bool IsEnabledForCurrentExecutable()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            string? registeredCommand = key?.GetValue(ValueName) as string;
            return string.Equals(
                registeredCommand,
                BuildCommand(Application.ExecutablePath),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            return false;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("无法打开当前用户的 Windows 启动项。");

        if (enabled)
        {
            key.SetValue(
                ValueName,
                BuildCommand(Application.ExecutablePath),
                RegistryValueKind.String);
        }
        else
        {
            string? registeredCommand = key.GetValue(ValueName) as string;
            if (string.Equals(
                    registeredCommand,
                    BuildCommand(Application.ExecutablePath),
                    StringComparison.OrdinalIgnoreCase))
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
    }

    private static string BuildCommand(string executablePath)
    {
        return $"\"{Path.GetFullPath(executablePath)}\"";
    }
}
