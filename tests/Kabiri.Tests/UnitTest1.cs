using Kabiri.Application;
using Kabiri.Domain;

namespace Kabiri.Tests;

public class DeduplicationTests
{
    [Fact]
    public void SameQuadruple_Twice_SecondIsDuplicate_FirstWins()
    {
        var store = new InMemoryReadingStore();
        var r = new Reading("PUMP-01", "temperature", DateTimeOffset.Parse("2025-06-01T08:00:00Z"), 70, 1);
        Assert.True(store.TryAdd(r));
        Assert.False(store.TryAdd(r with { Value = 999 })); // same key, different value → duplicate
        Assert.Equal(70, store.All().Single().Value); // first-wins
    }

    [Fact]
    public void Parser_Rejects_Malformed_And_Semantic_Invalid()
    {
        Assert.Null(ReadingParser.ParseLine("{not json").Reading);
        Assert.Null(ReadingParser.ParseLine("""{"deviceId":"","metric":"pressure","ts":"2025-06-01T08:00:00Z","value":5,"seq":1}""").Reading);
        Assert.Null(ReadingParser.ParseLine("""{"deviceId":"P1","metric":null,"ts":"2025-06-01T08:00:00Z","value":1,"seq":1}""").Reading);
        Assert.Null(ReadingParser.ParseLine("""{"deviceId":"P1","metric":"temperature","ts":"2025-06-01T08:00:05","value":1,"seq":1}""").Reading); // missing Z
        Assert.NotNull(ReadingParser.ParseLine("""{"deviceId":"P1","metric":"temperature","ts":"2025-06-01T08:00:00Z","value":1,"seq":1}""").Reading);
    }
}

public class RuleEvaluationTests
{
    private static Rule Gt(string id = "r1") => new(id, id, true, "temperature", null, RuleOperator.GreaterThan, Threshold: 75);

    [Fact]
    public void Stateless_Operators_Classify()
    {
        var ev = new RuleEvaluator();
        var ok = new Reading("P1", "temperature", DateTimeOffset.UtcNow, 80, 1);
        var bad = new Reading("P1", "temperature", DateTimeOffset.UtcNow, 70, 2);
        Assert.True(ev.Classify(ok, [Gt()]).IsAcceptable);
        var c = ev.Classify(bad, [Gt()]);
        Assert.False(c.IsAcceptable);
        Assert.Equal("r1", c.Violations.Single().RuleId);
    }

    [Fact]
    public void Between_And_Device_Scoping()
    {
        var ev = new RuleEvaluator();
        var rule = new Rule("rb", "rb", true, "pressure", "PUMP-01", RuleOperator.Between, Min: 4, Max: 10);
        var other = new Reading("PUMP-02", "pressure", DateTimeOffset.UtcNow, 99, 1);
        Assert.True(ev.Classify(other, [rule]).IsAcceptable); // device-scoped → not applicable
    }

    [Fact]
    public void SustainedAbove_OutOfOrder_SameResult_As_Sorted()
    {
        var rule = new Rule("s1", "s1", true, "temperature", "PUMP-01", RuleOperator.SustainedAbove, Threshold: 80, DurationSeconds: 30);
        var t0 = DateTimeOffset.Parse("2025-06-01T08:00:00Z");
        var ordered = Enumerable.Range(0, 5).Select(i => new Reading("PUMP-01", "temperature", t0.AddSeconds(i * 10), 85, i)).ToList();
        var shuffled = ordered.OrderBy(_ => Guid.NewGuid()).ToList(); // simulate file disorder (order varies, result must not)
        var a = SustainedEvaluator.DetectEpisodes(rule, ordered);
        var b = SustainedEvaluator.DetectEpisodes(rule, shuffled);
        Assert.Single(a); Assert.Single(b);
        Assert.Equal(a[0].Start, b[0].Start);
        Assert.Equal(40, (a[0].End - a[0].Start).TotalSeconds);
    }

    [Fact]
    public void SustainedAbove_ShortSpike_NoEpisode()
    {
        var rule = new Rule("s1", "s1", true, "temperature", "PUMP-01", RuleOperator.SustainedAbove, Threshold: 80, DurationSeconds: 30);
        var t0 = DateTimeOffset.Parse("2025-06-01T08:00:00Z");
        var readings = new[]
        {
            new Reading("PUMP-01", "temperature", t0, 85, 1),
            new Reading("PUMP-01", "temperature", t0.AddSeconds(10), 85, 2),
            new Reading("PUMP-01", "temperature", t0.AddSeconds(20), 70, 3), // drops before 30s
        };
        Assert.Empty(SustainedEvaluator.DetectEpisodes(rule, readings));
    }
}

public class AlertCooldownTests
{
    [Fact]
    public void Episodes_Within_Cooldown_Collapse_To_One()
    {
        var svc = new AlertService();
        var rule = new Rule("s1", "s1", true, "temperature", "P1", RuleOperator.SustainedAbove, Threshold: 80, DurationSeconds: 10, CooldownSeconds: 300);
        var t0 = DateTimeOffset.Parse("2025-06-01T08:00:00Z");
        var episodes = new List<(Rule, DateTimeOffset, DateTimeOffset, double)>
        {
            (rule, t0, t0.AddSeconds(15), 85),
            (rule, t0.AddMinutes(2), t0.AddMinutes(2).AddSeconds(15), 86), // within 5-min cooldown → suppressed
            (rule, t0.AddMinutes(10), t0.AddMinutes(10).AddSeconds(15), 87), // beyond → new alert
        };
        var alerts = svc.BuildAlerts(episodes);
        Assert.Equal(2, alerts.Count);
    }
}

public class IdempotencyTests
{
    [Fact]
    public void ReRun_SameFile_NoDuplicates()
    {
        var svc = new ProcessingService(new InMemoryReadingStore(), new InMemoryResultStore(), new InMemoryAlertStore());
        var rules = new List<Rule> { new("r1", "r1", true, "temperature", null, RuleOperator.GreaterThan, Threshold: 75) };
        var lines = new[]
        {
            """{"deviceId":"P1","metric":"temperature","ts":"2025-06-01T08:00:00Z","value":80,"seq":1}""",
            """{"deviceId":"P1","metric":"temperature","ts":"2025-06-01T08:00:00Z","value":80,"seq":1}""", // dup in-file
            "not json",
        };
        var r1 = svc.ProcessLines(lines, rules);
        var r2 = svc.ProcessLines(lines, rules); // re-run
        Assert.Equal(1, r1.Stored); Assert.Equal(1, r1.Duplicates);
        Assert.Equal(0, r2.Stored); // second run stores nothing new
        Assert.Equal(r1.Acceptable, r2.Acceptable);
    }
}

public class AggregationTests
{
    [Fact]
    public void Aggregate_Uses_Acceptable_Only_And_Buckets()
    {
        var from = DateTimeOffset.Parse("2025-06-01T08:00:00Z");
        var readings = new[]
        {
            new Reading("P1", "temperature", from.AddSeconds(10), 10, 1),
            new Reading("P1", "temperature", from.AddSeconds(20), 20, 2),
            new Reading("P1", "temperature", from.AddSeconds(70), 30, 3),
        };
        var buckets = AggregationService.Aggregate(readings, from, from.AddMinutes(3), 60);
        Assert.Equal(2, buckets.Count);
        Assert.Equal(2, buckets[0].Count); Assert.Equal(15, buckets[0].Avg);
        Assert.Equal(30, buckets[1].Avg);
        // empty bucket omitted by design
    }
}
