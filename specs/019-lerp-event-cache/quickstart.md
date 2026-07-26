# Quickstart: Last Lerp Event Cache

Verifies the feature end to end. Run from repo root with the Aspire AppHost (which starts WebAPI + Worker + WASM).

## Prerequisites

- .NET 10 SDK
- `dotnet run` on `src/ChefKnifeStudios.TransitJazz.AppHost` (or the existing local run flow)

## Build & run

```pwsh
dotnet build ChefKnifeStudios.TransitJazz.sln
dotnet run --project src/ChefKnifeStudios.TransitJazz.AppHost
```

## Step 1 — Cold start: endpoint returns empty 200 (FR-004 / US2)

Immediately after the WebAPI starts, before the Worker's first ~10s poll publishes a batch:

```pwsh
curl -k https://localhost:<webapi-port>/transit/last-batch
```

**Expect**: `200 OK` with body `[]`. No error, no 204/404/503.

## Step 2 — After a push: endpoint returns the latest batch (FR-001/002, US1/US3)

Wait for at least one Worker reconciliation cycle (watch the WebAPI log for `Relayed N events from worker`), then:

```pwsh
curl -k https://localhost:<webapi-port>/transit/last-batch
```

**Expect**: `200 OK` with a non-empty JSON array of `EventEnvelope`, `eventType: "RouteNearestPointBatchEvent"`, `payload.batchRecords` populated. Re-run after the next cycle — the body reflects the newer batch (latest wins).

## Step 3 — Buses appear immediately on load (US1 / SC-001, SC-002)

1. Ensure at least one batch has been published (Step 2 returns data).
2. Open the map in a fresh browser tab (or hard-reload).
3. **Expect**: buses render essentially within the page load — not after a multi-second blank wait. In the browser console you should see the snapshot fetched and `HandleVehicleBatchAsync` forwarding records to the animator before the first SignalR `ReceiveBatch` arrives.

To make the win obvious, reload right after a push: pre-feature the map sits empty up to ~10s; post-feature buses are present at load.

## Step 4 — Smooth transition to the first live push (FR-006 / SC-004)

After Step 3, leave the tab open across the next Worker cycle.

**Expect**: when the first live `ReceiveBatch` arrives, buses continue smoothly — no flicker, no duplicate marker, no teleport. (The animator keys on `vehicleId` and re-interpolating the same prior→current pair is idempotent.)

## Step 5 — Cold-start client load (US2)

1. Restart the WebAPI/Worker.
2. Open the map *before* the first push.
3. **Expect**: map loads cleanly, no vehicles, no error/spinner-forever. When the first push lands, buses appear normally.

## Step 6 — No upstream fetch on read (FR-007 / SC-005)

Hammer the endpoint a few times in quick succession:

```pwsh
1..5 | ForEach-Object { curl -k https://localhost:<webapi-port>/transit/last-batch | Out-Null }
```

**Expect**: WebAPI logs show **no** new GTFS-RT fetches or Worker calls triggered by these reads (the Worker's own ~10s poll cadence is unchanged). Responses are served from memory.

## Step 7 — Server unit tests

```pwsh
dotnet test src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests
```

**Expect**: all pass —
- `LastBatchCacheTests`: empty/non-null cold start, latest-wins, defensive null, concurrent set/read never torn (FR-008).
- `WorkerTransitHubTests`: `PublishBatch` caches the batch **and** relays `ReceiveBatch` (FR-001/002/010).

Integration tests are out of scope; endpoint HTTP behavior is verified by Steps 1–2 and 6 above. See `contracts/tests.md` for the full assertion list and scope boundary.

## Acceptance mapping

| Step | Validates |
|------|-----------|
| 1 | FR-004; US2 AC-1 |
| 2 | FR-001, FR-002; US3 AC-1 |
| 3 | FR-005, FR-009; US1 AC-1; SC-001, SC-002 |
| 4 | FR-006; US1 AC-2; SC-004 |
| 5 | US2 AC-1/AC-2; SC-003 |
| 6 | FR-007; SC-005 |
| 7 | FR-001, FR-002, FR-008, FR-010 (automated unit tests) |
