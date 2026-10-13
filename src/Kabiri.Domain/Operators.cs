namespace Kabiri.Domain;

/// <summary>Stateless per-reading operator. Return null = pass, non-null = violation reason.</summary>
public interface IRuleOperator
{
    RuleOperator Operator { get; }
    string? Evaluate(Reading reading, Rule rule);
}

public sealed class GreaterThanOperator : IRuleOperator
{
    public RuleOperator Operator => RuleOperator.GreaterThan;
    public string? Evaluate(Reading r, Rule rule)
    {
        if (rule.Threshold is null) return $"Rule {rule.Id} missing threshold.";
        return r.Value > rule.Threshold ? null : $"Value {r.Value} is not > {rule.Threshold}.";
    }
}

public sealed class GreaterThanOrEqualOperator : IRuleOperator
{
    public RuleOperator Operator => RuleOperator.GreaterThanOrEqual;
    public string? Evaluate(Reading r, Rule rule)
    {
        if (rule.Threshold is null) return $"Rule {rule.Id} missing threshold.";
        return r.Value >= rule.Threshold ? null : $"Value {r.Value} is not >= {rule.Threshold}.";
    }
}

public sealed class LessThanOperator : IRuleOperator
{
    public RuleOperator Operator => RuleOperator.LessThan;
    public string? Evaluate(Reading r, Rule rule)
    {
        if (rule.Threshold is null) return $"Rule {rule.Id} missing threshold.";
        return r.Value < rule.Threshold ? null : $"Value {r.Value} is not < {rule.Threshold}.";
    }
}

public sealed class LessThanOrEqualOperator : IRuleOperator
{
    public RuleOperator Operator => RuleOperator.LessThanOrEqual;
    public string? Evaluate(Reading r, Rule rule)
    {
        if (rule.Threshold is null) return $"Rule {rule.Id} missing threshold.";
        return r.Value <= rule.Threshold ? null : $"Value {r.Value} is not <= {rule.Threshold}.";
    }
}

public sealed class EqualOperator : IRuleOperator
{
    public RuleOperator Operator => RuleOperator.Equal;
    public string? Evaluate(Reading r, Rule rule)
    {
        if (rule.Expected is null) return $"Rule {rule.Id} missing expected.";
        var tol = rule.Tolerance ?? 0d;
        return Math.Abs(r.Value - rule.Expected.Value) <= tol ? null : $"Value {r.Value} != {rule.Expected} (tol {tol}).";
    }
}

public sealed class BetweenOperator : IRuleOperator
{
    public RuleOperator Operator => RuleOperator.Between;
    public string? Evaluate(Reading r, Rule rule)
    {
        if (rule.Min is null || rule.Max is null) return $"Rule {rule.Id} missing min/max.";
        return r.Value >= rule.Min && r.Value <= rule.Max ? null : $"Value {r.Value} not in [{rule.Min},{rule.Max}].";
    }
}

/// <summary>Open for extension: register new operator here, no change to evaluation loop.</summary>
public sealed class OperatorRegistry
{
    private readonly Dictionary<RuleOperator, IRuleOperator> _map;
    public OperatorRegistry(IEnumerable<IRuleOperator>? extra = null)
    {
        IRuleOperator[] defaults =
        [
            new GreaterThanOperator(), new GreaterThanOrEqualOperator(),
            new LessThanOperator(), new LessThanOrEqualOperator(),
            new EqualOperator(), new BetweenOperator()
        ];
        _map = defaults.ToDictionary(o => o.Operator);
        if (extra is not null) foreach (var o in extra) _map[o.Operator] = o;
    }
    public IRuleOperator Get(RuleOperator op) => _map[op];
    public bool TryGet(RuleOperator op, out IRuleOperator? handler) => _map.TryGetValue(op, out handler);
}
