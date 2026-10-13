namespace Kabiri.Domain;

public sealed record RuleViolation(string RuleId, string RuleName, string Reason);
public sealed record Classification(Reading Reading, bool IsAcceptable, IReadOnlyList<RuleViolation> Violations);
public sealed record Alert(string RuleId, string DeviceId, string Metric, DateTimeOffset StartTs, DateTimeOffset EndTs, double Peak)
{
    public string Key => $"{RuleId}|{DeviceId}|{Metric}|{StartTs:O}";
}
