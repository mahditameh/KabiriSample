using Kabiri.Domain;
using Microsoft.Extensions.Logging;

namespace Kabiri.Application;

/// <summary>Counts printed at the end of processing; numbers always reflect the real input.</summary>
/// <param name="TotalLines">Non-blank lines read from the file.</param>
/// <param name="Parsed">Lines that parsed AND passed semantic validation.</param>
/// <param name="Stored">Newly stored readings this run (0 on idempotent re-run).</param>
/// <param name="Duplicates">Lines skipped as duplicate (deviceId, metric, ts, seq) — first-wins.</param>
/// <param name="Invalid">Malformed or semantically invalid lines rejected without crashing.</param>
/// <param name="RulesLoaded">Enabled + disabled rules loaded from rules.json.</param>
/// <param name="Evaluations">Stateless rule evaluations performed this run (new readings only).</param>
/// <param name="Acceptable">Total acceptable readings in store (cumulative).</param>
/// <param name="Unacceptable">Total unacceptable readings in store (cumulative).</param>
/// <param name="Violations">Total stateless rule violations in store (cumulative).</param>
/// <param name="Alerts">Total alerts in store (cumulative).</param>
public sealed record ProcessingReport(
    int TotalLines, int Parsed, int Stored, int Duplicates, int Invalid,
    int RulesLoaded, int Evaluations, int Acceptable, int Unacceptable,
    int Violations, int Alerts);

public sealed class ProcessingService
{
    private readonly IReadingStore _readings;
    private readonly IResultStore _results;
    private readonly IAlertStore _alerts;
    private readonly RuleEvaluator _evaluator;
    private readonly AlertService _alertService;
    private readonly ILogger<ProcessingService>? _log;

    public ProcessingService(IReadingStore readings, IResultStore results, IAlertStore alerts,
        RuleEvaluator? evaluator = null, AlertService? alertService = null, ILogger<ProcessingService>? log = null)
    {
        _readings = readings; _results = results; _alerts = alerts;
        _evaluator = evaluator ?? new RuleEvaluator();
        _alertService = alertService ?? new AlertService();
        _log = log;
    }

    public ProcessingReport ProcessLines(IEnumerable<string> lines, IReadOnlyList<Rule> rules)
    {
        var (total, parsed, stored, duplicates, invalid, fresh) = Ingest(lines);
        var (evaluations, violations) = ClassifyFresh(fresh, rules);
        var storedReadings = _readings.All(); // single snapshot: avoids N queries + multiple enumeration
        foreach (var a in _alertService.BuildAlerts(DetectAllEpisodes(rules, storedReadings)))
        {
            if (_alerts.TryAdd(a)) _log?.LogInformation("Alert {Rule} {Dev}/{Met} [{S}..{E}] peak={P}", a.RuleId, a.DeviceId, a.Metric, a.StartTs, a.EndTs, a.Peak);
        }

        var all = _results.All();
        var report = new ProcessingReport(total, parsed, stored, duplicates, invalid, rules.Count,
            evaluations, all.Count(c => c.IsAcceptable), all.Count(c => !c.IsAcceptable),
            all.Sum(c => c.Violations.Count), _alerts.All().Count);
        _log?.LogInformation("Report: {@Report}", report);
        return report;
    }

    private (int Total, int Parsed, int Stored, int Duplicates, int Invalid, List<Reading> Fresh) Ingest(IEnumerable<string> lines)
    {
        int total = 0, parsed = 0, stored = 0, duplicates = 0, invalid = 0;
        var fresh = new List<Reading>(); // newly stored this run (stateless eval only on new → idempotent)
        int lineNo = 0;
        foreach (var line in lines)
        {
            lineNo++;
            if (string.IsNullOrWhiteSpace(line)) continue;
            total++;
            var outcome = ReadingParser.ParseLine(line);
            if (outcome.Reading is null) { invalid++; _log?.LogWarning("Rejected line {No}: {Err}", lineNo, outcome.Error); continue; }
            parsed++;
            if (_readings.TryAdd(outcome.Reading)) { stored++; fresh.Add(outcome.Reading); }
            else { duplicates++; }
        }
        return (total, parsed, stored, duplicates, invalid, fresh);
    }

    private (int Evaluations, int Violations) ClassifyFresh(List<Reading> fresh, IReadOnlyList<Rule> rules)
    {
        int evaluations = 0, violations = 0;
        foreach (var r in fresh)
        {
            var c = _evaluator.Classify(r, rules);
            evaluations++;
            violations += c.Violations.Count;
            _results.TryAdd(c); // idempotent by reading key
        }
        return (evaluations, violations);
    }

    // Stateful: full re-scan over stored snapshot (batch, idempotent via alert key).
    private static List<(Rule, DateTimeOffset, DateTimeOffset, double)> DetectAllEpisodes(IReadOnlyList<Rule> rules, IReadOnlyList<Reading> stored)
    {
        var episodes = new List<(Rule, DateTimeOffset, DateTimeOffset, double)>();
        foreach (var rule in rules.Where(r => r.Enabled && r.IsStateful))
        {
            if (rule.DeviceId is not null)
            {
                episodes.AddRange(SustainedEvaluator.DetectEpisodes(rule, stored));
            }
            else
            {
                // Expand null-device rule per actual device carrying the metric.
                foreach (var dev in stored.Where(r => r.Metric.Equals(rule.Metric, StringComparison.OrdinalIgnoreCase)).Select(r => r.DeviceId).Distinct())
                {
                    var scoped = rule with { DeviceId = dev };
                    episodes.AddRange(SustainedEvaluator.DetectEpisodes(scoped, stored.Where(r => r.DeviceId == dev)));
                }
            }
        }
        return episodes;
    }
}
