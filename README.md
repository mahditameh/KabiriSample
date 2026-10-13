# Kabiri — Sensor Ingestion, Stateful Rule Engine & Alerting

.NET 10 / C# backend: file ingestion → validation/dedup → rule evaluation → sustained alerts (cooldown) → aggregation API. SQLite persistence, idempotent re-run.

## Run & Test

```powershell
dotnet build
dotnet test
dotnet run --project src/Kabiri.Api
# API: http://localhost:5121
# POST /api/process?file=<path>  — re-process (idempotent)
# GET  /api/aggregates?deviceId=PUMP-02&metric=temperature&from=2025-06-01T08:00:00Z&to=2025-06-01T09:00:00Z&bucketSeconds=60
# GET  /api/alerts
```

Seed rules: `src/Kabiri.Api/rules.json` (also copied to repo root). Data: `readings.jsonl`.

## Real Processing Report (2150-line file)

```
Total=2150 Parsed=2141 Stored=2103 Dup=38 Invalid=9 Rules=8 Eval=2103 Accept=1062 Unaccept=1041 Viol=1880 Alerts=1
```

- 9 invalid: 1 truncated JSON, 2 bad values (null, "NaN"), 1 null metric, 2 bad deviceId (missing/empty), 1 null seq, 1 impossible date (06-31), 1 ts without `Z`.
- 38 duplicate keys (first-wins, `INSERT OR fail` on `(deviceId,metric,ts,seq)`).
- 1 alert: `pump02-heat-sustained PUMP-02/temperature [08:26:40..08:34:50Z] peak=74.483`. Spec example rule (`PUMP-01 >80`) correctly yields 0 episodes (PUMP-01 max ≈70.5).
- Re-run with existing `kabiri.db` stores 0 new rows (UNIQUE keys on readings/results/alerts) — proven by `IdempotencyTests.ReRun_SameFile_NoDuplicates`.

## Key Decisions & Trade-offs

- **Architecture:** Layered Clean-lite (`Domain` pure → `Application` use-cases → `Infrastructure` SQLite → `Api` thin). All domain logic (validation, dedup policy, applicability, evaluation, sustained scan, cooldown, classification, aggregation) lives in Domain/Application, never controllers.
- **Storage SQLite + `Microsoft.Data.Sqlite`:** zero-setup file DB, UNIQUE constraints enforce idempotency at the storage level. Justified over in-memory (survives restart, still trivial for 2k rows). In-memory stores kept for unit tests.
- **Rule model:** data-driven (`id,name,enabled,metric,deviceId?,operator,params`); enabling/disabling = JSON change. New operator = new `IRuleOperator` + registry entry, no change to `RuleEvaluator` loop (Open/Closed).
- **State & ordering (`SustainedAbove`):** batch sort-then-scan per `(device,metric)` on event-time `Ts` (O(n log n), ~2k rows trivial). Deterministic under out-of-order input; late arrivals handled by full re-scan. Streaming incremental state rejected: more complex, order-sensitive, harder to make idempotent. Window: start = first `>threshold`, end = last `>threshold` before drop to `<=threshold` (or end of data); violated iff `end-start >= durationSeconds`.
- **Classification vs alerts:** stateless rules → per-reading acceptable/unacceptable; stateful rules → alerts only (per spec §7), they do not flip classification. No applicable rule → acceptable.
- **Cooldown:** 5 min per `(rule,device,metric)` on episode `startTs` gaps; one episode = one alert. Null-device sustained rules expanded per actual device.
- **Aggregation:** over acceptable only; buckets `[from,to)`, empty buckets omitted (documented choice); `bucketSeconds>0` required.
- **Duplicate policy:** first-wins (file order), justified: deterministic + preserves original observation; last-wins would silently prefer later file position with no event-time meaning.

## Adding a New Operator

1. Implement `IRuleOperator` in `Domain/Operators.cs` (e.g. `SustainedBelowOperator` or stateless).
2. Register in `OperatorRegistry` defaults.
3. Add JSON fields to `rules.json` + loader mapping in `RulesJsonLoader`.
4. Add unit test. No edits to `RuleEvaluator.Classify` needed (stateful ops need an evaluator + alert path like `SustainedEvaluator`).

## AI Disclosure

AI coding assistant (OpenCode / Muse Spark) used for scaffolding, analysis scripts, and drafting docs; all code built, tested, and executed locally (`dotnet build`, `dotnet test` 9/9 pass, live `dotnet run` report above).
