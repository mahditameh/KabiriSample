namespace Kabiri.Domain;

/// <summary>Available rule operators. Stateless ones evaluate per reading; <see cref="RuleOperator.SustainedAbove"/> is stateful (alerts).</summary>
public enum RuleOperator
{
    /// <summary>Value must be strictly greater than threshold.</summary>
    GreaterThan,
    /// <summary>Value must be greater than or equal to threshold.</summary>
    GreaterThanOrEqual,
    /// <summary>Value must be strictly less than threshold.</summary>
    LessThan,
    /// <summary>Value must be less than or equal to threshold.</summary>
    LessThanOrEqual,
    /// <summary>Value must equal expected within tolerance.</summary>
    Equal,
    /// <summary>Value must fall inside [min, max].</summary>
    Between,
    /// <summary>Stateful: value stays above threshold for at least durationSeconds of event time.</summary>
    SustainedAbove
}

/// <summary>Seed-data rule (from rules.json). Enabling/disabling is a data change, not a code change.</summary>
/// <param name="Id">Stable rule identifier, referenced by violations and alerts.</param>
/// <param name="Name">Human-readable rule name.</param>
/// <param name="Enabled">Disabled rules are ignored everywhere.</param>
/// <param name="Metric">Metric this rule applies to.</param>
/// <param name="DeviceId">Optional device scope; null means all devices carrying <paramref name="Metric"/>.</param>
/// <param name="Operator">Rule operator (stateless, or stateful SustainedAbove).</param>
/// <param name="Threshold">Required by GreaterThan/LessThan variants and SustainedAbove.</param>
/// <param name="Min">Between lower bound (inclusive).</param>
/// <param name="Max">Between upper bound (inclusive).</param>
/// <param name="Expected">Equal target value.</param>
/// <param name="Tolerance">Equal comparison tolerance (default 0).</param>
/// <param name="DurationSeconds">SustainedAbove: minimum above-threshold event-time span.</param>
/// <param name="CooldownSeconds">Alert dedup window per (rule, device, metric). Default 300.</param>
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
    /// <summary>Whether this rule applies to the given reading (metric + optional device match).</summary>
    public bool AppliesTo(Reading r) =>
        Enabled &&
        string.Equals(r.Metric, Metric, StringComparison.OrdinalIgnoreCase) &&
        (DeviceId is null || string.Equals(r.DeviceId, DeviceId, StringComparison.OrdinalIgnoreCase));

    /// <summary>True for operators evaluated over time windows (alerts), not per reading.</summary>
    public bool IsStateful => Operator == RuleOperator.SustainedAbove;
}
