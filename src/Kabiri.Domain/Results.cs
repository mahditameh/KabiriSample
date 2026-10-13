namespace Kabiri.Domain;

/// <summary>One stateless rule hit on a reading.</summary>
/// <param name="RuleId">Violated rule identifier.</param>
/// <param name="RuleName">Violated rule name.</param>
/// <param name="Reason">Human-readable explanation (actual vs expected).</param>
public sealed record RuleViolation(string RuleId, string RuleName, string Reason);

/// <summary>Per-reading verdict from stateless rules. Stateful rules never change this; they raise <see cref="Alert"/>s.</summary>
/// <param name="Reading">The evaluated reading.</param>
/// <param name="IsAcceptable">True when no applicable enabled stateless rule was violated (or none applied).</param>
/// <param name="Violations">One entry per violated rule; empty when acceptable.</param>
public sealed record Classification(Reading Reading, bool IsAcceptable, IReadOnlyList<RuleViolation> Violations);

/// <summary>One sustained episode (cooldown-deduplicated). Re-running the same input never creates a copy (keyed).</summary>
/// <param name="RuleId">Sustained rule identifier.</param>
/// <param name="DeviceId">Device the episode was observed on.</param>
/// <param name="Metric">Metric the episode was observed on.</param>
/// <param name="StartTs">Event time the sustained condition began (first above-threshold reading).</param>
/// <param name="EndTs">Event time it ended (last above-threshold reading, or end of observed data).</param>
/// <param name="Peak">Maximum value observed inside the episode.</param>
public sealed record Alert(string RuleId, string DeviceId, string Metric, DateTimeOffset StartTs, DateTimeOffset EndTs, double Peak)
{
    /// <summary>Idempotency key: (rule, device, metric, startTs).</summary>
    public string Key => $"{RuleId}|{DeviceId}|{Metric}|{StartTs:O}";
}
