using System.Text.Json;

namespace MiVibe.Remote.Tray;

/// <summary>Strict, bounded parsing for authenticated helper messages.</summary>
internal sealed record KeyBridgeMessage(string Type, string? Phase = null, int Step = 0,
    string? Key = null, long Sequence = 0)
{
    public static KeyBridgeMessage Parse(string line)
    {
        using JsonDocument document = JsonDocument.Parse(line, new JsonDocumentOptions { MaxDepth = 4 });
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Expected an object.");
        }

        var fields = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonProperty property in root.EnumerateObject())
        {
            if (!fields.Add(property.Name))
            {
                throw new JsonException("Duplicate message field.");
            }
        }

        string type = ReadString(root, "type");
        if (type == "heartbeat" && fields.SetEquals(["type"]))
        {
            return new(type);
        }

        if (type == "key" && fields.SetEquals(["type", "key", "sequence"]))
        {
            string key = ReadString(root, "key");
            if (key is not ("back" or "volume_up" or "volume_down") ||
                !root.TryGetProperty("sequence", out JsonElement sequence) ||
                sequence.ValueKind != JsonValueKind.Number ||
                !sequence.TryGetInt64(out long number) || number <= 0)
            {
                throw new JsonException("Invalid key or sequence.");
            }

            return new(type, Key: key, Sequence: number);
        }

        if (type == "status" && fields.IsSubsetOf(["type", "phase", "detail", "step"]) &&
            fields.IsSupersetOf(["type", "phase", "detail"]))
        {
            string phase = ReadString(root, "phase");
            if (phase is not ("connecting" or "calibrating" or "active" or "reconnecting" or "error" or "stopped") ||
                ReadString(root, "detail").Length > 256)
            {
                throw new JsonException("Invalid status.");
            }

            int step = 0;
            if (root.TryGetProperty("step", out JsonElement progress))
            {
                if (progress.ValueKind != JsonValueKind.Number ||
                    !progress.TryGetInt32(out step) || step is < 0 or > 2)
                {
                    throw new JsonException("Invalid calibration step.");
                }
            }
            else if (phase == "calibrating")
            {
                throw new JsonException("Missing calibration step.");
            }

            return new(type, phase, step);
        }

        throw new JsonException("Unknown type or message fields.");
    }

    private static string ReadString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.String)
        {
            throw new JsonException("Missing or non-string field.");
        }

        return value.GetString()!;
    }
}
