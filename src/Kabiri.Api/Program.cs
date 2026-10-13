using Kabiri.Application;
using Kabiri.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o => o.SwaggerDoc("v1", new() { Title = "Kabiri Sensor Ingestion API", Version = "v1" }));
builder.Services.AddSingleton(_ =>
{
    var dbPath = Path.Combine(builder.Environment.ContentRootPath, "kabiri.db");
    return new SqliteStores(dbPath);
});
builder.Services.AddSingleton<IReadingStore>(sp => sp.GetRequiredService<SqliteStores>());
builder.Services.AddSingleton<IResultStore>(sp => sp.GetRequiredService<SqliteStores>());
builder.Services.AddSingleton<IAlertStore>(sp => sp.GetRequiredService<SqliteStores>());
builder.Services.AddSingleton<ProcessingService>();
var app = builder.Build();

string RulesPath() => Path.Combine(app.Environment.ContentRootPath, "rules.json");
string DefaultDataPath()
{
    var local = Path.Combine(app.Environment.ContentRootPath, "readings.jsonl");
    if (File.Exists(local)) return local;
    var up2 = Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, "..", "..", "..", "readings.jsonl"));
    return up2;
}

ProcessingReport RunProcess(string? file)
{
    file ??= DefaultDataPath();
    var rules = RulesJsonLoader.Load(RulesPath());
    var svc = app.Services.GetRequiredService<ProcessingService>();
    var lines = File.ReadLines(file);
    var report = svc.ProcessLines(lines, rules);
    Console.WriteLine($"Total={report.TotalLines} Parsed={report.Parsed} Stored={report.Stored} Dup={report.Duplicates} Invalid={report.Invalid} Rules={report.RulesLoaded} Eval={report.Evaluations} Accept={report.Acceptable} Unaccept={report.Unacceptable} Viol={report.Violations} Alerts={report.Alerts}");
    return report;
}

// Auto-process on startup so `dotnet run` prints the required report.
try { RunProcess(null); }
catch (Exception ex) { Console.WriteLine($"Startup processing skipped: {ex.Message}"); }

app.MapPost("/api/process", (string? file) => RunProcess(file));
app.MapGet("/api/alerts", (IAlertStore s) => s.All());
app.MapGet("/api/aggregates", (IResultStore results, string deviceId, string metric, DateTimeOffset from, DateTimeOffset to, int bucketSeconds = 60) =>
{
    var acceptable = results.Acceptable()
        .Where(c => c.Reading.DeviceId == deviceId && c.Reading.Metric.Equals(metric, StringComparison.OrdinalIgnoreCase))
        .Select(c => c.Reading);
    return AggregationService.Aggregate(acceptable, from, to, bucketSeconds);
});
app.MapGet("/", () => "Kabiri Sensor Ingestion API. POST /api/process, GET /api/aggregates, GET /api/alerts")
    .ExcludeFromDescription();

app.UseSwagger();
app.UseSwaggerUI();

app.Run();
