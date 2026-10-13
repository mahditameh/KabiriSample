using System.Reflection;
using Kabiri.Api;
using Kabiri.Application;
using Kabiri.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new() { Title = "Kabiri Sensor Ingestion API", Version = "v1" });
    // Surface XML docs from Api + Application + Domain on schemas and (for named handlers) endpoints.
    foreach (var asm in new[] { typeof(Program).Assembly, typeof(ProcessingService).Assembly, typeof(Kabiri.Domain.Reading).Assembly })
    {
        var xml = Path.Combine(AppContext.BaseDirectory, $"{asm.GetName().Name}.xml");
        if (File.Exists(xml)) o.IncludeXmlComments(xml);
    }
});
builder.Services.AddSingleton(_ =>
{
    var dbPath = Path.Combine(builder.Environment.ContentRootPath, "kabiri.db");
    return new SqliteStores(dbPath);
});
builder.Services.AddSingleton<IReadingStore>(sp => sp.GetRequiredService<SqliteStores>());
builder.Services.AddSingleton<IResultStore>(sp => sp.GetRequiredService<SqliteStores>());
builder.Services.AddSingleton<IAlertStore>(sp => sp.GetRequiredService<SqliteStores>());
builder.Services.AddSingleton<ProcessingService>();
builder.Services.AddSingleton(_ =>
{
    string RulesPath() => Path.Combine(builder.Environment.ContentRootPath, "rules.json");
    string DefaultDataPath()
    {
        var local = Path.Combine(builder.Environment.ContentRootPath, "readings.jsonl");
        if (File.Exists(local)) return local;
        return Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "..", "..", "readings.jsonl"));
    }
    return new FileLocations(RulesPath(), DefaultDataPath());
});
var app = builder.Build();

// Auto-process on startup so `dotnet run` prints the required report.
try
{
    var svc = app.Services.GetRequiredService<ProcessingService>();
    var files = app.Services.GetRequiredService<FileLocations>();
    ApiHandlers.Process(null, svc, files);
}
catch (Exception ex) { Console.WriteLine($"Startup processing skipped: {ex.Message}"); }

app.MapPost("/api/process", ApiHandlers.Process)
    .WithSummary("Process readings file")
    .WithDescription("Ingests readings.jsonl, cleans duplicates/invalid rows, evaluates rules, raises sustained alerts. Idempotent.")
    .Produces<Kabiri.Application.ProcessingReport>(200);
app.MapGet("/api/alerts", ApiHandlers.GetAlerts)
    .WithSummary("List alerts")
    .WithDescription("Sustained-rule alerts with start/end timestamps and peak values, oldest first.")
    .Produces<IReadOnlyList<Kabiri.Domain.Alert>>(200);
app.MapGet("/api/aggregates", (string deviceId, string metric, DateTimeOffset from, DateTimeOffset to, IResultStore results, int bucketSeconds = 60) =>
        ApiHandlers.GetAggregates(deviceId, metric, from, to, bucketSeconds, results))
    .WithSummary("Bucketed aggregates")
    .WithDescription("Per-time-bucket count/avg/min/max over acceptable readings only. Empty buckets omitted.")
    .Produces<IReadOnlyList<Kabiri.Application.Bucket>>(200);
app.MapGet("/", () => "Kabiri Sensor Ingestion API. POST /api/process, GET /api/aggregates, GET /api/alerts")
    .ExcludeFromDescription();

app.UseSwagger();
app.UseSwaggerUI();

app.Run();
