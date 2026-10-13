namespace Kabiri.Domain;

public sealed record Reading(
    string DeviceId,
    string Metric,
    DateTimeOffset Ts,
    double Value,
    long Seq)
{
    public string Key => $"{DeviceId}|{Metric}|{Ts:O}|{Seq}";
}
