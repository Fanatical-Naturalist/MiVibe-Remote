using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace MiVibe.Remote.GattProbe;

internal static class AudioRouteInspector
{
    public static AudioRouteStatus InspectAndPrint()
    {
        using var enumerator = new MMDeviceEnumerator();
        string renderMultimedia = GetDefaultName(enumerator, DataFlow.Render, Role.Multimedia);
        string renderCommunications = GetDefaultName(enumerator, DataFlow.Render, Role.Communications);
        string captureMultimedia = GetDefaultName(enumerator, DataFlow.Capture, Role.Multimedia);
        string captureCommunications = GetDefaultName(enumerator, DataFlow.Capture, Role.Communications);
        string[] activeRenderNames = GetActiveNames(enumerator, DataFlow.Render);
        string[] activeCaptureNames = GetActiveNames(enumerator, DataFlow.Capture);
        string[] airPodsRenderNames = activeRenderNames.Where(IsAirPods).ToArray();

        bool cableIsDefaultCapture = IsCableOutput(captureMultimedia) &&
                                     IsCableOutput(captureCommunications);
        bool airPodsDetected = airPodsRenderNames.Length > 0;
        bool airPodsIsDefaultRender = IsAirPods(renderMultimedia) ||
                                      IsAirPods(renderCommunications);
        bool airPodsIsDefaultCapture = IsAirPods(captureMultimedia) ||
                                       IsAirPods(captureCommunications);

        Console.WriteLine("Audio route status:");
        Console.WriteLine($"  Input  (multimedia):     {captureMultimedia}");
        Console.WriteLine($"  Input  (communications): {captureCommunications}");
        Console.WriteLine($"  Output (multimedia):     {renderMultimedia}");
        Console.WriteLine($"  Output (communications): {renderCommunications}");

        if (cableIsDefaultCapture)
        {
            Console.WriteLine(
                "  PASS: CABLE Output is the default input for both roles; " +
                "Xiaomi remote audio has priority.");
        }
        else
        {
            Console.WriteLine(
                "  WARNING: Set CABLE Output as both the default recording device and " +
                "default communications recording device before using Voice.");
        }

        if (airPodsDetected)
        {
            Console.WriteLine($"  AirPods output detected: {string.Join(" | ", airPodsRenderNames)}");
            Console.WriteLine(
                airPodsIsDefaultRender
                    ? "  PASS: AirPods is selected for Windows audio output."
                    : "  INFO: AirPods is available but is not a current default output.");
        }
        else
        {
            Console.WriteLine("  INFO: No active AirPods output endpoint was detected.");
        }

        if (airPodsIsDefaultCapture)
        {
            Console.WriteLine(
                "  WARNING: AirPods is a default input. Windows may open its microphone " +
                "and switch Bluetooth playback into a voice/Hands-Free profile.");
        }

        string[] airPodsCaptureNames = activeCaptureNames.Where(IsAirPods).ToArray();
        if (airPodsCaptureNames.Length > 0 && !airPodsIsDefaultCapture)
        {
            Console.WriteLine(
                "  PASS: AirPods microphone exists but is not selected as a default input.");
        }

        return new AudioRouteStatus(
            cableIsDefaultCapture,
            airPodsDetected,
            airPodsIsDefaultRender,
            airPodsIsDefaultCapture);
    }

    private static string GetDefaultName(
        MMDeviceEnumerator enumerator,
        DataFlow flow,
        Role role)
    {
        try
        {
            using MMDevice device = enumerator.GetDefaultAudioEndpoint(flow, role);
            return device.FriendlyName;
        }
        catch (COMException)
        {
            return "<none>";
        }
    }

    private static string[] GetActiveNames(MMDeviceEnumerator enumerator, DataFlow flow)
    {
        MMDeviceCollection devices = enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active);
        var names = new List<string>(devices.Count);
        foreach (MMDevice device in devices)
        {
            names.Add(device.FriendlyName);
            device.Dispose();
        }

        return names.ToArray();
    }

    private static bool IsAirPods(string name) =>
        name.Contains("AirPods", StringComparison.OrdinalIgnoreCase);

    private static bool IsCableOutput(string name) =>
        name.Equals("CABLE Output", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("CABLE Output ", StringComparison.OrdinalIgnoreCase);
}

internal readonly record struct AudioRouteStatus(
    bool CableIsDefaultCapture,
    bool AirPodsDetected,
    bool AirPodsIsDefaultRender,
    bool AirPodsIsDefaultCapture);
