using System.Text.Json;
using Kabiri.Application;
using Kabiri.Domain;
using Microsoft.Data.Sqlite;

namespace Kabiri.Infrastructure;

public sealed class SqliteStores : IReadingStore, IResultStore, IAlertStore
{
    private readonly string _connStr;
    public SqliteStores(string dbPath)
    {
        _connStr = $"Data Source={dbPath}";
        EnsureSchema();
    }

    private SqliteConnection Open() { var c = new SqliteConnection(_connStr); c.Open(); return c; }

    private void EnsureSchema()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS readings(key TEXT PRIMARY KEY, device TEXT NOT NULL, metric TEXT NOT NULL, ts TEXT NOT NULL, value REAL NOT NULL, seq INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS results(rkey TEXT PRIMARY KEY, acceptable INTEGER NOT NULL, violations TEXT NOT NULL DEFAULT '[]');
            CREATE TABLE IF NOT EXISTS alerts(key TEXT PRIMARY KEY, rule TEXT NOT NULL, device TEXT NOT NULL, metric TEXT NOT NULL, startTs TEXT NOT NULL, endTs TEXT NOT NULL, peak REAL NOT NULL);
            """;
        cmd.ExecuteNonQuery();
    }

    public bool TryAdd(Reading r) => TryInsert(
        "INSERT INTO readings(key,device,metric,ts,value,seq) VALUES($k,$d,$m,$t,$v,$s)",
        ("$k", r.Key), ("$d", r.DeviceId), ("$m", r.Metric),
        ("$t", r.Ts.ToString("O")), ("$v", (object)r.Value), ("$s", r.Seq));

    IReadOnlyList<Reading> IReadingStore.All()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT device,metric,ts,value,seq FROM readings";
        var list = new List<Reading>();
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
            list.Add(new Reading(rd.GetString(0), rd.GetString(1), DateTimeOffset.Parse(rd.GetString(2)), rd.GetDouble(3), rd.GetInt64(4)));
        return list;
    }

    public int Count
    {
        get { using var c = Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT COUNT(*) FROM readings"; return Convert.ToInt32(cmd.ExecuteScalar()); }
    }

    // IResultStore (explicit to avoid All() clash)
    bool IResultStore.TryAdd(Classification cl) => TryInsert(
        "INSERT INTO results(rkey,acceptable,violations) VALUES($k,$a,$v)",
        ("$k", cl.Reading.Key), ("$a", cl.IsAcceptable ? 1 : 0),
        ("$v", JsonSerializer.Serialize(cl.Violations)));

    IReadOnlyList<Classification> IResultStore.All() => LoadResults(null);
    public IReadOnlyList<Classification> Acceptable() => LoadResults(true);

    // IAlertStore
    bool IAlertStore.TryAdd(Alert a) => TryInsert(
        "INSERT INTO alerts(key,rule,device,metric,startTs,endTs,peak) VALUES($k,$r,$d,$m,$s,$e,$p)",
        ("$k", a.Key), ("$r", a.RuleId), ("$d", a.DeviceId), ("$m", a.Metric),
        ("$s", a.StartTs.ToString("O")), ("$e", a.EndTs.ToString("O")), ("$p", (object)a.Peak));

    /// <summary>Shared idempotent insert: UNIQUE violation → false (duplicate), not an error.</summary>
    private bool TryInsert(string sql, params (string Name, object Value)[] parameters)
    {
        try
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value);
            cmd.ExecuteNonQuery();
            return true;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19) { return false; }
    }

    IReadOnlyList<Alert> IAlertStore.All()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT rule,device,metric,startTs,endTs,peak FROM alerts ORDER BY startTs";
        var list = new List<Alert>();
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
            list.Add(new Alert(rd.GetString(0), rd.GetString(1), rd.GetString(2), DateTimeOffset.Parse(rd.GetString(3)), DateTimeOffset.Parse(rd.GetString(4)), rd.GetDouble(5)));
        return list;
    }

    private List<Classification> LoadResults(bool? onlyAcceptable)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT r.device,r.metric,r.ts,r.value,r.seq, s.acceptable, s.violations FROM results s
            JOIN readings r ON r.key = s.rkey
            """ + (onlyAcceptable == true ? " WHERE s.acceptable=1" : "");
        var list = new List<Classification>();
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            var reading = new Reading(rd.GetString(0), rd.GetString(1), DateTimeOffset.Parse(rd.GetString(2)), rd.GetDouble(3), rd.GetInt64(4));
            var viols = JsonSerializer.Deserialize<List<RuleViolation>>(rd.GetString(6)) ?? new();
            list.Add(new Classification(reading, rd.GetInt32(5) == 1, viols));
        }
        return list;
    }
}

public static class RulesJsonLoader
{
    public static List<Rule> Load(string path)
    {
        var json = File.ReadAllText(path);
        var doc = JsonDocument.Parse(json);
        var list = new List<Rule>();
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            string Text(string p) => el.TryGetProperty(p, out var x) ? x.GetString() ?? "" : "";
            double? Number(string p) => el.TryGetProperty(p, out var x) && x.ValueKind == JsonValueKind.Number ? x.GetDouble() : null;
            int? Whole(string p) => el.TryGetProperty(p, out var x) && x.ValueKind == JsonValueKind.Number ? x.GetInt32() : null;
            string? Optional(string p) => el.TryGetProperty(p, out var x) ? x.ValueKind == JsonValueKind.Null ? null : x.GetString() : null;
            var op = Enum.Parse<RuleOperator>(Text("operator"), true);
            list.Add(new Rule(
                Id: Text("id"), Name: Optional("name") ?? Text("id"),
                Enabled: !el.TryGetProperty("enabled", out var e) || e.GetBoolean(),
                Metric: Text("metric"), DeviceId: Optional("deviceId"),
                Operator: op, Threshold: Number("threshold"), Min: Number("min") ?? Number("minValue"),
                Max: Number("max") ?? Number("maxValue"), Expected: Number("expected") ?? Number("value"),
                Tolerance: Number("tolerance"),
                DurationSeconds: Whole("durationSeconds"),
                CooldownSeconds: Whole("cooldownSeconds") ?? 300));
        }
        return list;
    }
}
