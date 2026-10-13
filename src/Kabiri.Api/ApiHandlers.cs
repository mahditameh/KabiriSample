using Kabiri.Application;
using Kabiri.Domain;
using Kabiri.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Kabiri.Api;

/// <summary>HTTP handlers. XML docs on these methods feed Swagger descriptions.</summary>
public static class ApiHandlers
{
    /// <summary>Process a readings file (ingest → validate → dedup → evaluate → alert). Idempotent: re-running the same file stores nothing new.</summary>
    /// <param name="file">Optional absolute .jsonl path. Defaults to the bundled readings.jsonl.</param>
    /// <param name="svc">Processing pipeline (injected).</param>
    /// <param name="rulesPath">Resolved rules.json path (injected).</param>
    /// <returns>Processing counts reflecting the real input.</returns>
    /// <response code="200">Processing completed; see report counts.</response>
    public static ProcessingReport Process(
        [FromQuery] string? file,
        [FromServices] ProcessingService svc,
        [FromServices] FileLocations files)
    {
        var rules = RulesJsonLoader.Load(files.RulesPath);
        var report = svc.ProcessLines(System.IO.File.ReadLines(file ?? files.DefaultDataPath), rules);
        Console.WriteLine($"Total={report.TotalLines} Parsed={report.Parsed} Stored={report.Stored} Dup={report.Duplicates} Invalid={report.Invalid} Rules={report.RulesLoaded} Eval={report.Evaluations} Accept={report.Acceptable} Unaccept={report.Unacceptable} Viol={report.Violations} Alerts={report.Alerts}");
        return report;
    }

    /// <summary>List stored sustained-rule alerts, oldest first.</summary>
    /// <param name="alerts">Alert store (injected).</param>
    /// <returns>All alerts with start/end timestamps and peak values.</returns>
    /// <response code="200">The alert list (possibly empty).</response>
    public static IReadOnlyList<Alert> GetAlerts([FromServices] IAlertStore alerts) => alerts.All();

    /// <summary>Per-bucket aggregates over acceptable readings only (unacceptable excluded).</summary>
    /// <param name="deviceId">Device to aggregate, e.g. PUMP-02.</param>
    /// <param name="metric">Metric to aggregate, e.g. temperature.</param>
    /// <param name="from">Inclusive range start (UTC).</param>
    /// <param name="to">Exclusive range end (UTC).</param>
    /// <param name="bucketSeconds">Bucket width in seconds; must be &gt; 0. Default 60.</param>
    /// <param name="results">Classification store (injected).</param>
    /// <returns>One entry per non-empty bucket: start, count, avg, min, max.</returns>
    /// <response code="200">Bucket list; empty when no acceptable readings fall in range.</response>
    public static IReadOnlyList<Bucket> GetAggregates(
        [FromQuery] string deviceId,
        [FromQuery] string metric,
        [FromQuery] DateTimeOffset from,
        [FromQuery] DateTimeOffset to,
        [FromQuery] int bucketSeconds,
        [FromServices] IResultStore results)
    {
        var acceptable = results.Acceptable()
            .Where(c => c.Reading.DeviceId == deviceId && c.Reading.Metric.Equals(metric, StringComparison.OrdinalIgnoreCase))
            .Select(c => c.Reading);
        return AggregationService.Aggregate(acceptable, from, to, bucketSeconds);
    }
}

/// <summary>Resolved on-disk locations for seed rules and default input data.</summary>
/// <param name="RulesPath">rules.json path.</param>
/// <param name="DefaultDataPath">Fallback readings.jsonl path.</param>
public sealed record FileLocations(string RulesPath, string DefaultDataPath);
