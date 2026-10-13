# TASKS — Sensor Ingestion, Stateful Rule Engine & Alerting

Each task = one meaningful commit. Order optimized for 2-day scope.

## Phase 0 — Setup
- [ ] T0: Init .NET 10 solution + projects (`Domain`, `Application`, `Infrastructure`, `Api`, `Tests`), README skeleton, `.gitignore`
- [ ] T1: Add `readings.jsonl` sample + create `rules.json` seed (stateless + 1 SustainedAbove), config loading

## Phase 1 — Ingestion & Cleaning
- [ ] T2: JSONL reader — line-by-line, malformed line catch + count, no crash
- [ ] T3: Validation — required fields, types, finite value, ISO-8601 UTC ts, int seq; separate invalid list
- [ ] T4: Deduplication by `(deviceId,metric,ts,seq)` — first-wins + idempotent store (unique constraint), test

## Phase 2 — Rule Engine
- [ ] T5: Rule model + `rules.json` loader (enabled flag, optional deviceId), applicability filter
- [ ] T6: Stateless operators via Strategy/Registry: `Gt, Gte, Lt, Lte, Eq, Between` + unit tests
- [ ] T7: `SustainedAbove` batch evaluator — sort by event-time per `(deviceId,metric)`, window start/end, peak; document streaming-vs-batch
- [ ] T8: Classification — acceptable iff all applicable pass; violation with ruleId+reason; no-rule → acceptable

## Phase 3 — Alerting & Storage
- [ ] T9: Alert store — `ruleId,deviceId,metric,startTs,endTs,peak` + 5-min cooldown dedup per `(rule,device,metric)`, test
- [ ] T10: SQLite (EF Core) schema + repos — readings/results/alerts, idempotent re-run test (run twice, zero new rows)

## Phase 4 — API & Report
- [ ] T11: Aggregation `GET` — `deviceId,metric,from,to,bucketSeconds` → `bucketStart,count,avg,min,max` over acceptable only; empty-bucket policy documented + test
- [ ] T12: Processing report + logging — all required counts + event logs (rejections, episodes, alerts)

## Phase 5 — Quality & Delivery
- [ ] T13: Tests pass — dedup, rules + SustainedAbove out-of-order, cooldown, idempotency, aggregation
- [ ] T14: README — run/test, arch decisions, rule model, state/ordering, cooldown, aggregation, AI disclosure
- [ ] T15: Final verification — full run on real 2150-line file, capture report numbers, git history review

## Out of Scope (do not do)
- MQ/broker/K8s, auth, UI, prod deploy, rule CRUD API
