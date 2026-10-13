using Kabiri.Domain;

namespace Kabiri.Application;

/// <summary>Stateless evaluation only. Stateful rules produce alerts, not per-reading verdicts.</summary>
public sealed class RuleEvaluator
{
    private readonly OperatorRegistry _registry;
    public RuleEvaluator(OperatorRegistry? registry = null) => _registry = registry ?? new OperatorRegistry();

    public Classification Classify(Reading reading, IEnumerable<Rule> rules)
    {
        var violations = new List<RuleViolation>();
        foreach (var rule in rules)
        {
            if (!rule.AppliesTo(reading) || rule.IsStateful) continue;
            if (!_registry.TryGet(rule.Operator, out var op) || op is null) continue;
            var reason = op.Evaluate(reading, rule);
            if (reason is not null) violations.Add(new RuleViolation(rule.Id, rule.Name, reason));
        }
        return new Classification(reading, violations.Count == 0, violations);
    }
}

/// <summary>
/// Batch SustainedAbove: group by (device,metric), sort by event-time Ts, scan runs where value &gt; threshold.
/// Episode: [firstAboveTs .. lastAboveTs]; violated iff (last-first) &gt;= duration. End = first reading back at/below threshold (exclusive) or last observed above.
/// Out-of-order safe because we sort first; late arrivals handled by full re-scan (batch trade-off, O(n log n)).
/// </summary>
public static class SustainedEvaluator
{
    public static List<(Rule Rule, DateTimeOffset Start, DateTimeOffset End, double Peak)> DetectEpisodes(
        Rule rule, IEnumerable<Reading> readings)
    {
        var result = new List<(Rule, DateTimeOffset, DateTimeOffset, double)>();
        if (!rule.Enabled || rule.Operator != RuleOperator.SustainedAbove) return result;
        if (rule.Threshold is null || rule.DurationSeconds is null) return result;

        var sorted = readings
            .Where(r => rule.AppliesTo(r))
            .OrderBy(r => r.Ts).ThenBy(r => r.Seq)
            .ToList();

        DateTimeOffset? runStart = null;
        double peak = double.MinValue;
        DateTimeOffset lastAbove = default;

        void Flush(DateTimeOffset end)
        {
            if (runStart is null) return;
            if ((end - runStart.Value).TotalSeconds >= rule.DurationSeconds.Value)
                result.Add((rule, runStart.Value, end, peak));
            runStart = null; peak = double.MinValue;
        }

        foreach (var r in sorted)
        {
            if (r.Value > rule.Threshold.Value)
            {
                runStart ??= r.Ts;
                lastAbove = r.Ts;
                if (r.Value > peak) peak = r.Value;
            }
            else if (runStart is not null)
            {
                Flush(lastAbove); // end at last above reading (window of sustained above-threshold event time)
            }
        }
        if (runStart is not null) Flush(lastAbove);
        return result;
    }
}

public sealed class AlertService
{
    public List<Alert> BuildAlerts(IEnumerable<(Rule Rule, DateTimeOffset Start, DateTimeOffset End, double Peak)> episodes)
    {
        // Cooldown dedup per (rule,device,metric): episodes sorted by start; skip if start - lastEmittedStart < cooldown.
        var alerts = new List<Alert>();
        var lastStart = new Dictionary<string, DateTimeOffset>();
        foreach (var (rule, start, end, peak) in episodes.OrderBy(e => e.Start))
        {
            // device resolved by caller per-group; rule.DeviceId null means per actual device — episodes passed per device.
            var device = rule.DeviceId ?? throw new InvalidOperationException("Sustained episodes must be per-device (expand null-device rules per device).");
            var key = $"{rule.Id}|{device}|{rule.Metric}";
            if (lastStart.TryGetValue(key, out var prev) && (start - prev).TotalSeconds < rule.CooldownSeconds)
                continue;
            lastStart[key] = start;
            alerts.Add(new Alert(rule.Id, device, rule.Metric, start, end, peak));
        }
        return alerts;
    }
}
