namespace Kabiri.Domain;

/// <summary>Single validated sensor observation (already deduplicated).</summary>
/// <param name="DeviceId">Sensor device, e.g. PUMP-01.</param>
/// <param name="Metric">Measured metric: temperature, pressure or vibration.</param>
/// <param name="Ts">Event time of the observation (UTC).</param>
/// <param name="Value">Numeric sensor value (finite).</param>
/// <param name="Seq">Device sequence number; part of the idempotency key.</param>
public sealed record Reading(
    string DeviceId,
    string Metric,
    DateTimeOffset Ts,
    double Value,
    long Seq)
{
    /// <summary>Idempotency/dedup key: (deviceId, metric, ts, seq). First-wins.</summary>
    public string Key => $"{DeviceId}|{Metric}|{Ts:O}|{Seq}";
}
