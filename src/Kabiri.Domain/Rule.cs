namespace Kabiri.Domain;

public enum RuleOperator
{
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
    Equal,
    Between,
    SustainedAbove
}

public sealed record Rule(
    string Id,
    string Name,
    bool Enabled,
    string Metric,
    string? DeviceId,
    RuleOperator Operator,
    double? Threshold = null,
    double? Min = null,
    double? Max = null,
    double? Expected = null,
    double? Tolerance = null,
    int? DurationSeconds = null,
    int CooldownSeconds = 300)
{
    public bool AppliesTo(Reading r) =>
        Enabled &&
        string.Equals(r.Metric, Metric, StringComparison.OrdinalIgnoreCase) &&
        (DeviceId is null || string.Equals(r.DeviceId, DeviceId, StringComparison.OrdinalIgnoreCase));

    public bool IsStateful => Operator == RuleOperator.SustainedAbove;
}
