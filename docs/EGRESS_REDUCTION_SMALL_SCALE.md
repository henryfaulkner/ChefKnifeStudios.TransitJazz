# Egress Reduction at 500–2,000 Concurrent Users

> **Implemented by** `specs/051-egress-reduction/` — see that feature's spec/plan/tasks for the as-built design and delivery status.

**Date:** 2026-07-25
**Scope:** SignalR fan-out payload, `Server.WebAPI` HTTP responses, `Server.TransitDataWorker` publish path
**Target scale:** 500–2,000 concurrent users (the range the current single-replica architecture correctly serves)
**Companion doc:** `SCALABILITY_ASSESSMENT_100K.md` — that document assesses a 100k target and reaches very different conclusions. **This document supersedes it for planning at current scale.**

---

## Summary

At 500–2,000 users the system is **not** in trouble. Baseline egress is roughly **$40–500/month** depending on user count and which cities are popular. The goal here is keeping that number small and predictable, not averting a crisis.

The recommendations below are ordered by **return on effort**, not by raw savings. Several deliver large percentage reductions for well under a day of work. The expensive architectural changes from the 100k assessment — viewport scoping, Azure SignalR, worker extraction — are **explicitly not recommended at this scale** and are listed under "Deliberately not doing" with reasons.

**Recommended package: ~3–5 dev-days, cuts egress roughly 60–75%.**

---

## Baseline: where the bytes actually go

### Measured record shape

`RouteNearestPointBatchEvent.RouteNearestPointRecord` (`src/ChefKnifeStudios.TransitJazz.Shared/Events/RouteNearestPointBatchEvent.cs:32-44`) carries 11 MessagePack fields per vehicle:

| Key | Field | Type | Approx encoded bytes |
|---|---|---|---|
| 0 | `VehicleId` | string | 8–12 |
| 1 | `RouteJoinKey` | string | 5–10 |
| 2 | `PriorNearestLat` | double | 9 |
| 3 | `PriorNearestLon` | double | 9 |
| 4 | `CurrentNearestLat` | double | 9 |
| 5 | `CurrentNearestLon` | double | 9 |
| 6 | `DurationMs` | int | 2–5 |
| 7 | `SpeedMetersPerSec` | float? | 1–5 |
| 8 | `Bearing` | float? | 1–5 |
| 9 | `IsStale` | bool | 1 |
| 10 | `Category` | string | 4–10 |

**≈ 75–85 bytes per vehicle**, plus array/envelope overhead.

> **Estimate, not measurement.** These are derived from the MessagePack encoding of the declared contract. No wire capture has been taken. See "Step 0" below — measuring first is the single highest-value action in this document.

### Per-batch and monthly figures

Publish cadence is one batch per **10 seconds** (`Worker.cs:51`, `PeriodicTimer(TimeSpan.FromSeconds(10))`) = 6/min.

| City | Vehicles | Batch size |
|---|---|---|
| MARTA / RTD / SEPTA / MBTA | ~500–1,000 | ~40–80 KB |
| NYMTA | ~5,000 | ~400 KB |

Monthly egress, all users on one city, 6 batches/min × 60 × 24 × 30 = 259,200 batches/month:

| Users | Mid-size city (~60 KB) | NYMTA (~400 KB) |
|---|---|---|
| 500 | ~470 GB | ~3.1 TB |
| 1,000 | ~940 GB | ~6.2 TB |
| 2,000 | ~1.9 TB | ~12.4 TB |

At roughly **$0.087/GB** (Azure `eastus2`, first 10 TB tier, after the 100 GB monthly free allowance):

| Users | Mid-size city | NYMTA | Realistic mixed traffic |
|---|---|---|---|
| 500 | ~$32 | ~$270 | **~$40–130** |
| 1,000 | ~$75 | ~$540 | **~$90–260** |
| 2,000 | ~$160 | ~$1,080 | **~$180–520** |

> Azure list prices drift and vary by region/commitment. Verify in the Azure Pricing Calculator before budgeting. The *relative* savings below are robust; the absolute dollars are indicative.

**Key observation:** NYMTA is ~5–8× the cost of every other city combined. Several recommendations below target it specifically.

---

## Step 0 — Measure before optimizing (0.5 d)

Everything above is arithmetic on a contract, not observation. Before implementing anything, capture the real number.

Add a byte count at the publish site — `SignalRHubPublisher.PublishBatchAsync` (`SignalRHubPublisher.cs:94`), immediately before `InvokeAsync`:

```csharp
// Temporary instrumentation: actual MessagePack wire size per city per tick.
var wireBytes = MessagePackSerializer.Serialize(batch).Length;
_logger.LogInformation("Batch wire size: city={City} bytes={Bytes} records={Records}",
    city, wireBytes, batch.Sum(e => (e.Payload as RouteNearestPointBatchEvent)?.BatchRecords.Count ?? 0));
```

Note this serializes a second time purely to measure — acceptable for a short diagnostic window, remove or gate behind a flag afterward.

Alternatively, the existing telemetry pipeline already emits `PerCityCycle` rows (`Worker.cs:98-119`); adding a `batch_wire_bytes` column there gives durable history instead of a one-off log. That is the better option if you want to track the effect of each change below.

**Do this first.** It validates or corrects every estimate in this document, and it tells you whether NYMTA is really the outlier the arithmetic suggests.

---

## Recommendations, by return on effort

### R1 — Cut coordinate precision to 5 decimal places on the wire

**Effort: 0.5 d · Savings: ~15–20% of batch bytes · Risk: none**

Each record carries four doubles at 9 bytes each = **36 bytes, ~45% of the record**.

The values are *already* rounded to 5 decimals before transmission (`Worker.cs:431-434`):

```csharp
Math.Round(prior.NearestLat, 5),
Math.Round(prior.NearestLon, 5),
Math.Round(nearest.Lat, 5),
Math.Round(nearest.Lon, 5),
```

5 decimal places is ~1.1 m precision — appropriate for vehicle dots, and far finer than a map pixel at any usable zoom. But the values still ride as **full 64-bit doubles**, so the rounding saves nothing on the wire today.

**Fix:** transmit as scaled integers. `lat * 100000` fits comfortably in an `int` (±90 × 10⁵ = ±9,000,000; lon ±18,000,000). MessagePack encodes those in 4–5 bytes instead of 9.

- Change `Key(2)`–`Key(5)` from `double` to `int`
- Multiply at `Worker.cs:431-434`, divide on the client at `TransitMap.razor.cs:508-511`
- Saves ~16–20 bytes/record

This is the cleanest win available: pure encoding change, no behavior change, no information lost that the app was using.

**Wire-contract warning:** this is a breaking change across all three MessagePack hops (worker → hub → WASM client). See "Deployment constraints" below.

---

### R2 — Drop `PriorNearestLat`/`PriorNearestLon` from the wire

**Effort: 1–2 d · Savings: further ~20% · Risk: medium — needs care on cold start**

Two of the four coordinates are the vehicle's *previous* position. The client already received that value in the previous batch, as that vehicle's `Current`. It is redundant for any client that has been connected more than one tick.

The client uses it to animate the segment (`TransitMap.razor.cs:508-509`), so it cannot simply be deleted — the client must retain last-known position per `vehicleId` and use it as the segment origin.

Two cases need handling:

1. **Cold start / newly-visible vehicle** — no prior. The worker already handles this by sending prior == current with `DurationMs: 0` (`Worker.cs:461-468`). Keep sending both coordinates *only* for first-observation records, via a nullable field that is omitted otherwise.
2. **Replay on join** — `LastBatchCache.Current()` (`ILastBatchCache.cs:78-106`) serves a snapshot to joining clients. That snapshot must keep full prior/current, since the joining client has no history.

Combined with R1, the record drops from ~80 bytes to roughly **~45 bytes — a ~45% total reduction.**

Sequence this *after* R1 and after R6 (below), since all three touch the same contract and should ship as one wire change.

---

### R3 — Adaptive publish cadence for idle clients

**Effort: 1–2 d · Savings: 30–60% depending on user behavior · Risk: low**

Egress is linear in tick rate, and the 10-second cadence (`Worker.cs:51`) runs identically whether a user is actively watching the map or has left the tab open in a background window for six hours.

For an ambient audio-and-map experience, a meaningful share of sessions are long-lived and unattended. Those users cost exactly as much as engaged ones.

**Fix — two independent gates, in increasing order of effort:**

**(a) Pause on hidden tab (0.5 d, largest single win).** Use the Page Visibility API in JS interop: when `document.hidden` is true, the client leaves its city group; on visibility restore, it rejoins and gets a fresh snapshot from `LastBatchCache` (`TransitHub.cs:24-27`) — the replay path already exists and is exactly what's needed here. A backgrounded tab then costs **zero** egress.

Note the app's audio may intentionally continue in a hidden tab — that is a genuine product question. If ambient background listening is a supported use case, this gate must be conditioned on audio being muted, or dropped in favor of (b) alone.

**(b) Idle downgrade (1 d).** After N minutes without map interaction, move the connection to a slower group (e.g. `{city}:slow`) that the worker publishes to every 30–60s. Restore on interaction.

**(a) alone is likely worth more than any other single item in this document,** and it is half a day.

---

### R4 — Enable response compression for HTTP endpoints

**Effort: 0.5 d · Savings: 70–85% of REST egress · Risk: none**

No compression is registered anywhere in the server — confirmed by search for `ResponseCompression` / `UseResponseCompression` across `src/Server`: **no matches**.

The affected endpoint is significant. `GetAllRouteShapes` (`GtfsEndpoints.cs:76-112`) returns full route geometry — megabytes of JSON coordinate arrays — and **every client calls it on startup** (`ApplicationViewModel.cs:135`). Coordinate-dense JSON compresses extremely well; Brotli routinely achieves 85–90% on this shape of data.

```csharp
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
});
// ...
app.UseResponseCompression();
```

`EnableForHttps = true` is required — it defaults to `false`, and all production traffic is HTTPS (`containerApp.bicep:66`, `allowInsecure: false`).

This does **not** affect the SignalR/MessagePack path, which is separate. It is nonetheless close to free and cuts the single largest per-session HTTP transfer.

> Historically, compression over TLS raised BREACH/CRIME concerns. Those attacks require attacker-controlled input reflected into a response alongside a secret. This endpoint returns static public geometry with no secrets and no user input echoed back, so it is not applicable here.

---

### R5 — Cache the route-shape response (and consider CDN)

**Effort: 1 d cached / 3–4 d CDN · Savings: eliminates repeat-visit egress + large CPU win · Risk: low**

`GetAllRouteShapes` (`GtfsEndpoints.cs:100-106`) deserializes **every** stored shape blob into objects and re-serializes them **on every single request**:

```csharp
var features = allShapesResult.Value
    .Where(kvp => kvp.Key != GtfsStaticLoader.ReadyKey && ...)
    .Select(kvp => JsonSerializer.Deserialize<RouteShapeFeature>(kvp.Value, Shared.JsonOptions.Get()))
    .Where(f => f is not null)
    .ToList();
```

No caching, no `ETag`, no `Cache-Control`. The underlying data refreshes **once every 24 hours** (`Worker.cs:644`, `PeriodicTimer(TimeSpan.FromHours(24))`).

**Fix (1 d):** precompute the serialized bytes when `GtfsStaticLoader` finishes, store them, and serve with a strong `ETag` plus `Cache-Control: public, max-age=3600`. Returning `304 Not Modified` to returning visitors eliminates the transfer entirely — the largest per-session download, gone for anyone who reloads.

Combined with R4 this makes cold start dramatically cheaper in both bytes and CPU. `GetAllRoutes` (`GtfsEndpoints.cs:166-173`) has the identical anti-pattern and should get the same treatment.

Moving the blobs to CDN/blob storage entirely (3–4 d) is better still and removes the load from the container, but the cached-bytes version captures most of the benefit for a quarter of the effort. **At this scale, do the cheap version.**

---

### R6 — Omit `Category` per-record; send it per-route

**Effort: 0.5–1 d · Savings: ~5–8% · Risk: low**

`Category` (`Key(10)`) is a repeated string — `"bus"`, `"rail"`, `"streetcar"` — sent on **every vehicle, every tick**. It is a property of the *route*, not the vehicle: it is resolved from a route-keyed map at `Worker.cs:439` via `ResolveCategory(modeMap, routeJoinKey)`.

The client already has the full route catalog from `GetAllRouteShapes`, whose `RouteShapeProperties` carries `Category` (`TransitMap.razor.cs:431` reads `kvp.Value.Properties?.Category`). The per-record copy is redundant for any client with the catalog loaded.

**Caveat worth respecting:** the per-record value exists partly because route-join failures fall back to `"unknown"` rather than `"bus"` — a deliberate data-quality signal (`Worker.cs:352-356`, citing D6/FR-005/SC-006). Dropping the field must not silently reintroduce the mislabeling that comment guards against. Preserve it by sending `Category` **only** when it is `"unknown"` (i.e. differs from the catalog), leaving it null otherwise.

Small on its own; bundle it into the single wire change with R1 and R2.

---

### R7 — Suppress unchanged vehicles from the batch

**Effort: 2–3 d · Savings: 20–40%, highly feed-dependent · Risk: medium**

The worker already classifies each vehicle per tick (`Worker.cs:442-457`) into `movedCount`, `unchangedCount`, `stationaryCount`, `staleCount` — but **sends all of them regardless**.

A vehicle that is stale (`isStale: true`, no new GPS fix) or stationary at a layover contributes a full record conveying no new information. On many feeds this is a substantial fraction of the fleet, particularly off-peak.

**Fix:** omit records where `isStale` is true *and* position is unchanged from the previous tick. The client already handles idling correctly for stale vehicles, and absence can mean "unchanged."

**Two hazards that make this medium-risk:**

1. **`LastBatchCache` eviction.** `CityCache.Set` evicts vehicles not seen for `EvictAfterCycles = 3` data-carrying batches (`ILastBatchCache.cs:51-52`, `136-142`). Omitting a vehicle from the wire must **not** omit it from the cache update, or long-stationary vehicles vanish from the cold-start snapshot after 30 seconds. Keep feeding the full set to `LastBatchCache.Set`; filter only what goes to `Clients.Group`.

2. **The comment at `ILastBatchCache.cs:125-132`** explicitly warns that dropping stale records previously made the snapshot "synthetically all moving," causing the client to replay motion for stopped vehicles. That regression was fixed by *preserving* staleness. Any suppression must not undo it.

Because of these, sequence R7 **last**, after the wire changes are stable and measured. The savings are real but the blast radius is the largest in this document.

---

## Deliberately not doing

These come from `SCALABILITY_ASSESSMENT_100K.md` and are **wrong at this scale.** Recorded here so the decision is explicit rather than an oversight.

| Item | Why not, at 500–2,000 users |
|---|---|
| **Viewport-scoped tile groups** | 15–30 dev-days to save maybe $60–200/mo. Terrible trade. Reconsider only above ~5,000 users, or if NYMTA alone becomes dominant. |
| **Azure SignalR Service** | ~$50/mo minimum for capacity you will not touch. 500–2,000 connections × 20–80 KB = 10–160 MB, fits the existing `1Gi`. |
| **Extract Worker to its own Container App** | Only matters for `maxReplicas > 1`. You are staying at 1 replica; the co-hosting costs nothing here. |
| **`LastBatchCache` per-city locking** | Contention requires thousands of *simultaneous* joins. Not reachable at this scale. |
| **Multi-region / zone redundancy** | An availability question, not an egress one, and premature. |
| **Delta encoding / binary diffing** | R1+R2+R6 capture most of the benefit for a fraction of the complexity. Revisit only if measurement shows the record is still the dominant cost after those land. |

Two items from that document **do** still apply at this scale, for reasons unrelated to egress:

- **SWA Free → Standard** (`staticWebApp.bicep:32`) — the Free tier's 100 GB/month bandwidth cap will be exhausted by a multi-MB WASM bundle at these user counts, and the app stops serving. **~$9/mo, one line. Do it.** (R4 and R5 reduce pressure on this but do not remove the cap.)
- **Log Analytics wiring** (`containerAppsEnvironment.bicep:15-19` params declared but never passed by `main.bicep`, so `appLogsConfiguration` resolves to `null` and logs go nowhere) — needed to verify any of the above actually worked.

---

## Recommended package

| Phase | Items | Effort | Cumulative egress reduction |
|---|---|---|---|
| **0** | Measure (Step 0) + SWA Standard + Log Analytics | 1–1.5 d | — (enables everything) |
| **1** | R4 compression + R5 response caching | 1.5 d | REST egress −80%; cold start much cheaper |
| **2** | R3(a) hidden-tab pause | 0.5 d | −20–40% of total, depending on session behavior |
| **3** | R1 + R2 + R6 as one wire change | 2–3 d | Batch bytes −45–50% |
| **Total** | | **~5–6 d** | **~60–75% overall** |

Defer R3(b) and R7 until phases 0–3 are measured. They may prove unnecessary.

**Projected cost after the package**, mixed traffic:

| Users | Before | After |
|---|---|---|
| 500 | ~$40–130/mo | ~$15–40/mo |
| 1,000 | ~$90–260/mo | ~$30–75/mo |
| 2,000 | ~$180–520/mo | ~$55–150/mo |

Total infrastructure (container, SWA Standard, storage, Log Analytics, DNS) adds roughly **$65–90/mo** on top, largely flat across this range.

---

## Deployment constraints

**R1, R2, and R6 all change the MessagePack wire contract.** Per the established constraint recorded for feature 040, a wire-format change spans three hops that do not deploy atomically:

1. `SignalRHubPublisher` (worker) — `SignalRHubPublisher.cs:42`, `.AddMessagePackProtocol()`
2. `TransitHub` / `WorkerTransitHub` (server) — `Program.cs:55`
3. Blazor WASM client — `SignalRNotificationService.cs:88`

The server registers **only** MessagePack with no JSON fallback (`Program.cs:52-55`), so a version-mismatched peer is rejected at negotiation rather than degrading.

Worker and server currently ship together (same container, `Program.cs:161`), which simplifies this considerably — but **the WASM client is a separate deploy lane**, and cached clients may run old code against a new server.

Consequences:

- Ship all three field changes (R1, R2, R6) as **one** contract revision, not three. Each separate revision multiplies the compatibility window.
- Additive `[Key]` indices with nullable types tolerate skew better than changing existing key types. **R1 changes `Key(2)`–`Key(5)` from `double` to `int`, which is not backward-compatible** — plan for either a brief coordinated deploy or new key indices alongside deprecated old ones.
- If a MartaJazz-branded deployment still ships from a separate branch, this change must land on both.

---

## Open questions

1. **Does audio continue in a hidden tab?** Decides whether R3(a) can pause unconditionally or must be gated on mute state. This is a product call and is the main unknown blocking the highest-ROI item.
2. **Is NYMTA actually as dominant as the arithmetic suggests?** Step 0 answers it. If NYMTA is a small share of sessions, the mixed-traffic figures here are pessimistic and the whole package may be over-engineered for the real load.
3. **Are there long-lived idle sessions in practice?** Determines whether R3 is worth 30–60% or closer to 5%. Session-duration telemetry would settle it.
