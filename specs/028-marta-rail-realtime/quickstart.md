# Quickstart: MARTA Rail Realtime

Build-time spike first (confirm the live contract), then implement, then run the 7-step
verification plan from the design doc (§9).

## 0. Build-time confirmation spike (do this before wiring)

Re-probe the live endpoint to confirm the 2026-06-23 findings still hold:

```bash
# PowerShell or curl — base URL from the design doc; key optional (observed keyless 2026-06-23)
curl -s "https://developerservices.itsmarta.com:18096/itsmarta/railrealtimearrivals/developerservices/traindata?apiKey=$RAIL_KEY" > rail-sample.json
```

Confirm, from `rail-sample.json`:
- Each `TRAIN_ID` shows exactly **one** distinct `(LATITUDE,LONGITUDE)` across all its rows
  (OQ-1 / R2). If not → the live-position assumption changed; stop and revisit.
- `IS_REALTIME` values (expected mostly/all `"true"`).
- `EVENT_TIME` format is `MM/dd/yyyy hh:mm:ss tt`.
- Sample cadence: poll ~4× at ~11 s spacing; expect coarse/irregular deltas (R4) — confirms the
  animator-reuse decision.

## 1. Configuration

`appsettings.json` (committed) — **URL only, no key**:
```json
"Marta": {
  "RailRealtime": {
    "BaseUrl": "https://developerservices.itsmarta.com:18096/itsmarta/railrealtimearrivals/developerservices/traindata",
    "Enabled": true
  }
}
```

API key via user-secrets / environment (never committed):
```bash
dotnet user-secrets set "Marta:RailRealtime:ApiKey" "<key>"   # or env Marta__RailRealtime__ApiKey
```

## 2. Implement (worker-only)

Files under `src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/`:

1. `RailRealtime/RailRealtimeOptions.cs` — `BaseUrl`, `ApiKey`, `Enabled` (see data-model Entity 4).
2. `RailRealtime/RailArrivalDto.cs` — JSON DTO, all-string fields (data-model Entity 1).
3. `RailRealtime/RailRealtimeAdapter.cs` — `IRailRealtimeAdapter.FetchAsync`:
   fetch → filter `IS_REALTIME` → parse/skip → group by `TRAIN_ID` + contract-guard → emit
   `IReadOnlyList<FeedEntity>`; **best-effort** (catch-all → empty list). Respect `Enabled=false`.
4. `Program.cs` — register a named `"RailRealtimeApi"` `HttpClient`, bind
   `RailRealtimeOptions` from `Marta:RailRealtime`, register `IRailRealtimeAdapter` singleton; add
   the adapter to `Worker`'s primary constructor.
5. `Worker.cs` (`ExecuteAsync`, ~line 41-48) — fetch rail entities and merge per the
   [feed-adapter contract](./contracts/feed-adapter.md) (null-safe, additive).

## 3. Verification plan (design §9)

| Step | Check | How |
|------|-------|-----|
| V1 — Dedup guard (FR-013) | One coord per `TRAIN_ID`; loud warning if violated | Inspect adapter logs; unit-check grouping if a test harness is added |
| V2 — Snap correctness (FR-002) | Rail entities snap with small `SnapDistanceKm` to `RED/GOLD/BLUE/GREEN` shapes | Telemetry `snap` dataset via `mj-data-explorer`; filter `routeId in (RED,GOLD,BLUE,GREEN)` |
| V3 — Motion (FR-005/006) | Trains coast through `0,0,0` holds and re-anchor on steps; no freeze/teleport | Watch the running app; apply ETA-pacing refinement only if coasting looks noisy |
| V4 — Voice (FR-010) | Rail keys preload and play a trio voice | Confirm `instrumentFor` assigns; check `preload(routeIds)` receives rail keys |
| V5 — No bus regression (FR-009) | Bus counts in `CycleEventArgs` identical rail-off vs rail-on | Toggle `Marta:RailRealtime:Enabled`; compare `BusesProcessed` in `cycle` telemetry |
| V6 — Realtime filter (FR-004) | `IS_REALTIME != "true"` rows dropped before dedup | Synthetic/adapter-level check |
| V7 — Key safety (FR-012) | No key in committed config; app starts from env/secrets | `git grep` for the key; start app with key in user-secrets only |

## 4. Run

```bash
# from repo root — run the worker (Aspire AppHost or the worker project directly)
dotnet run --project src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker
```

Then open the client app and look for train markers on the four rail lines, gliding along track,
with buses unchanged.

## Acceptance

All seven steps pass, the spec's SC-001..SC-007 hold, and `git grep` finds no committed rail API
key. ETA-pacing, derived speed, and a rail-distinct voice family remain out of v1 scope.
