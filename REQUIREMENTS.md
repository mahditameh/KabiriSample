# Requirements — Sensor Ingestion, Stateful Rule Engine & Alerting

Source: `Technical_Task_—_Backend_Engineer_·_Sensor_Ingestion,_Stateful_Rule.pdf` + `readings.jsonl` (2150 lines).

## 1. Goal
Backend service that ingests numeric sensor readings from file, cleans messy data, evaluates against user-defined rules (incl. stateful operator), classifies acceptable/unacceptable, emits alerts for sustained violations, and exposes time-bucket aggregation over acceptable data.

Preferred stack: C# / .NET 8 or 10.

## 2. Out of Scope
- Message queues / brokers / Kubernetes — file input is enough.
- Auth, UI, production deployment.
- Rule-management CRUD — rules are seed data in `rules.json`.

## 3. Input Data
File: `readings.jsonl`, one JSON per line (~2150 readings).

Fields:
- `deviceId: string` e.g. `PUMP-01`
- `metric: string` e.g. `temperature | pressure | vibration`
- `ts: string` ISO-8601 UTC e.g. `2025-06-01T08:33:00Z`
- `value: number` e.g. `67.21`
- `seq: integer` e.g. `1199`

Messy-data rules:
- Duplicate key = `(deviceId, metric, ts, seq)`. Policy (first-wins vs last-wins) must be designed + justified.
- File is NOT time-sorted. Stateful evaluation must be deliberate + documented (sort-first / event-time windowing / other), incl. late arrivals.
- Malformed lines or semantically invalid readings → rejected + counted, no crash.
- Rule violation ≠ malformed data. Invalid/duplicate never reach evaluation, never counted as unacceptable.

Observed in real file:
- 2150 total lines, 1 truncated/malformed line (~line 1201).
- 38 duplicate keys (each x2).
- Time range ~08:00:05Z to 12:04:50Z on 2025-06-01, unordered.
- Devices: PUMP-01, COMP-01, FAN-03, PUMP-02 + anomalies (missing/empty deviceId).
- Semantic invalid samples: missing deviceId, empty deviceId, null metric, null value, string "NaN" value, invalid date 2025-06-31, ts without Z, null seq.
- Outliers that are violations (not invalid): FAN-03 temp 1000000, FAN-03 vibration -9999, etc.
- NOTE: `rules.json` referenced by task is missing in workspace — must be created.

## 4. Functional Requirements

### FR1 — Ingestion
- Read `readings.jsonl` line by line.
- Count total lines, parsed, stored, duplicates removed, invalid rejected.

### FR2 — Validation & Deduplication (domain layer)
- Validate: required fields, types, ts parseable ISO-8601 UTC, value finite number, seq integer.
- Deduplicate by `(deviceId, metric, ts, seq)` idempotently.

### FR3 — Rule Engine
Rule model: `id, name, enabled, metric, deviceId? (null = all devices), operator, params`.

Applicability: rule applies if `metric` matches and (`deviceId` null or equal).

Operators (min set):
- `GreaterThan, GreaterThanOrEqual, LessThan, LessThanOrEqual, Equal, Between` (stateless, per-reading)
- `SustainedAbove{threshold, durationSeconds}` (stateful, required)

Example:
```json
{"name":"Overheating Sustained","deviceId":"PUMP-01","metric":"temperature","operator":"SustainedAbove","threshold":80,"durationSeconds":30,"enabled":true}
```

SustainedAbove semantics:
- Violated when metric stays `> threshold` for >= `durationSeconds` of EVENT time.
- State per `(deviceId, metric)`.
- Window start = first crossing above threshold, end = drop back to `<= threshold` (or end of observed data).
- Must document: out-of-order handling, duration measurement, streaming vs batch + trade-offs.

Classification:
- `acceptable` if all applicable enabled rules pass; `unacceptable` if >=1 violated, with rule id + reason.
- No applicable rule → `acceptable` (document policy).
- Extensible: new operator without modifying existing evaluation code. Document how to add.

### FR4 — Alerting (stateful rules only)
- NOT per-reading. One alert per sustained episode: `ruleId, deviceId, metric, startTs, endTs, peak?`.
- Cooldown: after alert for `(rule, deviceId, metric)`, no new alert until 5 min elapsed (unless justified otherwise). Episodes separated by > cooldown → separate alerts.
- Stored + idempotent (re-run creates no copy).

### FR5 — Aggregation API
- `GET` endpoint (route/param names free) inputs: `deviceId, metric, from, to, bucketSeconds`.
- Output per equal bucket in `[from, to)`: `bucketStart, count, avg, min, max` over ACCEPTABLE only.
- Document: empty buckets omitted vs zero-count.

### FR6 — Idempotent Re-run
- Processing same file twice → no duplicate readings, rule results, alerts.
- Keying strategy stated + proven by test.

### FR7 — Processing Report
Print at end (real numbers): `totalLines, parsed, stored, duplicatesRemoved, invalidRejected, rulesLoaded, evaluationsPerformed, acceptable, unacceptable, violations, alertsGenerated`.
Log significant events (rejections, sustained episodes, alerts) at proper levels.

## 5. Non-Functional / Architecture
- Domain logic independent of infra (Clean / Layered / Vertical Slice — justify in README).
- Validation, dedup, applicability, evaluation, sustained-state, alerting, classification in domain/app layer, not controllers/repos.
- Extensible for new aggregations, input sources, operators. Reasonable time/memory. Document trade-offs.
- Storage free choice (in-memory, SQLite, ...) + justification.

## 6. Tests (minimum)
- Deduplication
- Rule evaluation incl. SustainedAbove with out-of-order data
- Alert cooldown
- Idempotent re-run
- Aggregation

## 7. Docs & Submission
- README: how to run/test, arch decisions + trade-offs, rule model + policy, state/ordering strategy, cooldown policy, aggregation behavior, AI usage disclosure.
- Git repo with incremental meaningful commits (not single commit) + online walkthrough.

## 8. Evaluation Criteria
1. Messy-data robustness 2. Rule-engine correctness 3. Alerting correctness 4. Idempotency 5. Aggregation correctness 6. Code cleanliness 7. Test quality 8. Design/architecture 9. README + explanation.
