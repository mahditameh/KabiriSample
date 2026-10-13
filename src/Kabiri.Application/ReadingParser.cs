using System.Text.Json;
using Kabiri.Domain;

namespace Kabiri.Application;

public static class ReadingParser
{
    public sealed record ParseOutcome(Reading? Reading, string? Error);

    public static ParseOutcome ParseLine(string line)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(line); }
        catch (Exception ex) { return new ParseOutcome(null, $"Malformed JSON: {ex.Message}"); }
        using (doc)
        {
            try
            {
                var root = doc.RootElement;
                string? deviceId = root.TryGetProperty("deviceId", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
                string? metric = root.TryGetProperty("metric", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
                string? tsRaw = root.TryGetProperty("ts", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
                double value;
                if (!root.TryGetProperty("value", out var v) || v.ValueKind is JsonValueKind.Null or JsonValueKind.String)
                    return new ParseOutcome(null, "Invalid value: must be finite number.");
                try { value = v.GetDouble(); }
                catch { return new ParseOutcome(null, "Invalid value: must be finite number."); }
                if (double.IsNaN(value) || double.IsInfinity(value))
                    return new ParseOutcome(null, "Invalid value: NaN/Infinity.");
                long seq;
                if (!root.TryGetProperty("seq", out var s) || s.ValueKind != JsonValueKind.Number || !s.TryGetInt64(out seq))
                    return new ParseOutcome(null, "Invalid seq: must be integer.");

                if (string.IsNullOrWhiteSpace(deviceId)) return new ParseOutcome(null, "Invalid deviceId: missing/empty.");
                if (string.IsNullOrWhiteSpace(metric)) return new ParseOutcome(null, "Invalid metric: missing/empty.");
                if (string.IsNullOrWhiteSpace(tsRaw)) return new ParseOutcome(null, "Invalid ts: missing.");
                if (!DateTimeOffset.TryParse(tsRaw, out var ts)) return new ParseOutcome(null, $"Invalid ts: '{tsRaw}'.");
                // Require UTC ISO-8601 with Z (per spec example); reject ambiguous local times.
                if (!tsRaw.EndsWith("Z", StringComparison.OrdinalIgnoreCase))
                    return new ParseOutcome(null, $"Invalid ts (must be UTC 'Z'): '{tsRaw}'.");

                return new ParseOutcome(new Reading(deviceId!, metric!, ts, value, seq), null);
            }
            catch (Exception ex) { return new ParseOutcome(null, $"Invalid record: {ex.Message}"); }
        }
    }
}
