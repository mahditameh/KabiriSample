using Kabiri.Domain;
using Microsoft.Extensions.Logging;

namespace Kabiri.Application;

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
        int total = 0, parsed = 0, stored = 0, duplicates = 0, invalid = 0;
        var fresh = new List<Reading>(); // newly stored this run (for stateless eval idempotency, eval only new)
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            total++;
            var outcome = ReadingParser.ParseLine(line);
            if (outcome.Reading is null) { invalid++; _log?.LogWarning("Rejected line: {Err}", outcome.Error); continue; }
            parsed++;
            if (_readings.TryAdd(outcome.Reading)) { stored++; fresh.Add(outcome.Reading); }
            else { duplicates++; }
        }

        int evaluations = 0, violations = 0;
        foreach (var r in fresh)
        {
            var c = _evaluator.Classify(r, rules);
            evaluations++;
            violations += c.Violations.Count;
            _results.TryAdd(c); // idempotent by reading key
        }

        // Stateful: full re-scan over all stored readings (batch, idempotent via alert key).
        var episodes = new List<(Rule, DateTimeOffset, DateTimeOffset, double)>();
        foreach (var rule in rules.Where(r => r.Enabled && r.IsStateful))
        {
            if (rule.DeviceId is not null)
            {
                episodes.AddRange(SustainedEvaluator.DetectEpisodes(rule, _readings.All()));
            }
            else
            {
                // Expand null-device rule per actual device carrying the metric.
                foreach (var dev in _readings.All().Where(r => r.Metric.Equals(rule.Metric, StringComparison.OrdinalIgnoreCase)).Select(r => r.DeviceId).Distinct())
                {
                    var scoped = rule with { DeviceId = dev };
                    episodes.AddRange(SustainedEvaluator.DetectEpisodes(scoped, _readings.All().Where(r => r.DeviceId == dev)));
                }
            }
        }
        var newAlerts = _alertService.BuildAlerts(episodes);
        int alertCount = 0;
        foreach (var a in newAlerts)
        {
            if (_alerts.TryAdd(a)) { alertCount++; _log?.LogInformation("Alert {Rule} {Dev}/{Met} [{S}..{E}] peak={P}", a.RuleId, a.DeviceId, a.Metric, a.StartTs, a.EndTs, a.Peak); }
        }

        var all = _results.All();
        var report = new ProcessingReport(total, parsed, stored, duplicates, invalid, rules.Count,
            evaluations, all.Count(c => c.IsAcceptable), all.Count(c => !c.IsAcceptable),
            all.Sum(c => c.Violations.Count), _alerts.All().Count);
        _log?.LogInformation("Report: {@Report}", report);
        return report;
    }
}
