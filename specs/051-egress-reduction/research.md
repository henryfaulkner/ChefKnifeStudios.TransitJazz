# Research: Egress Reduction at Current Scale (051)

All decisions verified against the codebase on 2026-07-25 (branch `051-egress-reduction`). Source analysis: `docs/EGRESS_REDUCTION_SMALL_SCALE.md`; its file:line claims were re-checked and hold, with corrections noted below.

---

## D1 — Wire-size measurement: permanent telemetry column, measured at the worker

**Decision**: Add `batch_wire_bytes` (`long?`, PerCityCycle-only, summed on FullCycle like the other counters) to `TelemetryEvent`. Measure in `Worker.ProcessSpatialReconciliationAsync` immediately before `transitHubPublisher.PublishBatchAsync(...)` (`Worker.cs:576`) by serializing the exact `List<EventEnvelope>` about to be published with `MessagePackSerializer.Serialize` into a pooled `ArrayBufferWriter<byte>` and recording `WrittenCount`. Carry the value on the `CityTickResult` into the existing `PerCityCycle` post (`Worker.cs:98-119`).

**Rationale**: The source doc offers two options — a temporary log line or a telemetry column — and itself concludes the column "gives durable history instead of a one-off log... the better option if you want to track the effect of each change." Since SC-001/SC-004/SC-005 are all defined against measured baselines and deltas, durable history is mandatory, so this is permanent, not gated. The double-serialization cost is one extra MessagePack pass per city per 10s tick (~60 KB mid-size, ~400 KB NYMTA) — microseconds of CPU; the pooled buffer avoids a 400 KB allocation per NYMTA tick on the LOH.

**Caveat (measurement vs. wire truth)**: This measures the MessagePack payload size, which is what the doc's estimates model. The actual socket bytes add SignalR framing and (if negotiated) per-message deflate — close enough for relative before/after comparison, which is all the success criteria need.

**Consistency obligation**: `TelemetryEvent.cs`'s own comment freezes the rule — the C# property name IS the parquet column name (Parquet.Net 5.6.1, no rename attribute), and the column set "MUST stay in sync with tools/telemetry-mcp/internal/validate/validate.go's kindNumeric allow-list." Adding `batch_wire_bytes` therefore REQUIRES the matching Go allow-list entry in the same change, or the MCP bridge rejects queries over the new column.

**Alternatives considered**: Temporary `LogInformation` at `SignalRHubPublisher.PublishBatchAsync` (doc's first sketch) — rejected: no durable history, and Phase 0's whole point is baselining. Hooking SignalR's serialization to avoid the second pass — rejected: no public seam; not worth custom protocol plumbing for microseconds.

---

## D2 — Observability infra: new Log Analytics module; SWA Free → Standard

**Decision**:
1. New `bicep/modules/logAnalytics.bicep` creating a `Microsoft.OperationalInsights/workspaces` resource (PerGB2018, default retention), outputting `customerId` and (via `listKeys`) the shared key.
2. `bicep/main.bicep` instantiates it and passes `logAnalyticsCustomerId`/`logAnalyticsSharedKey` into the existing `cae` module call (`main.bicep:187-195`). `containerAppsEnvironment.bicep` needs **no edits** — its params and the `empty()` conditional already exist (`containerAppsEnvironment.bicep:14-19, 26-32`); they are simply never supplied today, so `appLogsConfiguration` resolves to `null` and console logs are discarded.
3. `bicep/modules/staticWebApp.bicep:31-34`: `sku.name`/`sku.tier` `'Free'` → `'Standard'` (~$9/mo; removes the 100 GB/month bandwidth cap that a multi-MB WASM bundle at 500–2,000 users will exhaust — an availability fix, which is why it rides Phase 0, not a cost phase).

**Rationale**: FR-002/FR-003; without queryable logs none of the later phases can be verified in production (SC-001). The half-wired params prove intent — this completes it rather than redesigning it.

**Alternatives considered**: Managed-identity/AMPLS log routing — over-engineering at this scale; the environment-level `appLogsConfiguration` with a shared key is the platform-standard path and the key never leaves Bicep/ARM (`@secure()`).

---

## D3 — HTTP compression: framework middleware, Brotli + Gzip, HTTPS enabled

**Decision**: In `Server.WebAPI/Program.cs`: `AddResponseCompression` with `EnableForHttps = true`, `BrotliCompressionProvider` then `GzipCompressionProvider`, default MIME set (covers `application/json`); `app.UseResponseCompression()` registered before the endpoint mappings. Provider levels: Brotli `CompressionLevel.Fastest` (server CPU is 0.5 vCPU; Fastest still achieves ~80%+ on coordinate JSON).

**Rationale**: Verified: no `ResponseCompression` registration exists anywhere in `src/Server`. `GetAllRouteShapes` returns megabytes of coordinate-dense JSON to every client on startup (`ApplicationViewModel.cs:135`); this is the highest-ROI zero-risk change in the package. `EnableForHttps` is required because it defaults to `false` and production is HTTPS-only. BREACH/CRIME does not apply: the responses are static public geometry, no secrets, no reflected user input. The SignalR/MessagePack path is unaffected (WebSockets bypass response-compression middleware).

**Alternatives considered**: Pre-compressing the cached bytes once (store Brotli output in the D4 cache, serve with `Content-Encoding` directly) — attractive CPU-wise but requires hand-rolling `Accept-Encoding` negotiation and interacts badly with the middleware; deferred unless Phase 1 CPU measurement shows per-request Brotli matters at this request rate (it won't at 500–2,000 users).

---

## D4 — Route-response caching: precomputed bytes + strong ETag + 304, rebuilt on the 24h refresh

**Decision**: New `IRouteShapeResponseCache` singleton in WebAPI holding, per city and per endpoint (`all-shapes`, `all-routes`), an immutable `(byte[] Utf8Json, string ETag, DateTimeOffset GeneratedUtc)` entry. Populated by `GtfsStaticLoader` when it finishes building a city's shapes (initial load and every 24h refresh cycle), by serializing the aggregate response ONCE. ETag = strong quoted hash (e.g. hex SHA-256 truncated) of the bytes. `GetAllRouteShapes`/`GetAllRoutes` become: not-ready → 503 (unchanged); `If-None-Match` matches → `304`; else `Results.Bytes(entry.Utf8Json, "application/json")` with `ETag` and `Cache-Control: public, max-age=3600`. The per-route `GetRouteShape` and `GetSubwayStopOffsets` endpoints are left as-is (not on the startup critical path; keep the diff small).

**Rationale**: Verified anti-pattern at `GtfsEndpoints.cs:100-106` and `166-173`: every request deserializes every stored blob to `RouteShapeFeature` then re-serializes. Underlying data changes once per 24h (`Worker.cs:644` for the worker's copy; `GtfsStaticLoader` owns the WebAPI store). `max-age=3600` + ETag revalidation means a returning visitor transfers ~0 bytes for unchanged data (SC-002) while a daily refresh propagates within an hour worst-case, immediately on revalidation. Compression (D3) still applies to the cached bytes on the wire.

**Edge semantics**: cache entry absent for a requested city (e.g. unknown city param) → current behavior (empty feature list) preserved by serving an empty precomputed entry or falling through to the existing path; the "no city param = all cities" variant of `GetAllRouteShapes` gets its own cache entry keyed as `*`.

**Alternatives considered**: CDN/blob offload (doc R5 full version, 3–4 d) — rejected at this scale per the doc's own recommendation; the cached-bytes version captures most of the benefit. `IMemoryCache` with expiry — rejected: the data has an explicit producer (the loader); push-invalidation at load time is simpler and can never serve a stale-past-refresh body longer than the ETag window.

---

## D5 — Hidden-tab pause: Page Visibility interop + leave/rejoin city group, gated on mute

**Decision**:
- New `page-visibility.js` RCL module + `IPageVisibilityJsInterop` (lazy ES-module import with cache-bust GUID, `IAsyncDisposable`, callback via `DotNetObjectReference` — the exact `outside-click.js`/`TransitSynthJsInterop` idiom). Emits visibility-changed events with the current `document.hidden`.
- New hub method `TransitHub.LeaveCity(string city)` → `Groups.RemoveFromGroupAsync` + `HubMethods.LeaveCity` const. Additive — old clients never call it.
- `ISignalRNotificationService` gains `PauseAsync()`/`ResumeAsync()`: pause = invoke `LeaveCity` (connection stays open — a parked WebSocket costs only keepalive pings); resume = invoke `JoinCity`, which already replays `LastBatchCache.Current(city)` to the caller (`TransitHub.cs:24-27`) — the catch-up snapshot exists today, zero new server logic.
- Gate wiring in `TransitMap.razor.cs` (which already owns settings-event handling): pause only when `document.hidden && !Settings.IsAudioEnabled`; mute state read from `SettingsService` and tracked live via the existing `AudioSettingChangedEventArgs` on the event bus. Transitions to evaluate: tab hidden, tab visible, mute toggled while hidden (mute→pause now; unmute→resume now). Transitions are serialized behind a single in-flight guard, and desired-vs-actual joined state is reconciled after each transition so rapid toggling can't double-join or leak.

**Rationale**: FR-007/008/009. Leaving the group zeroes per-session egress server-side (SignalR group fan-out skips the connection entirely). The mute gate implements the spec's resolved product assumption: ambient background listening keeps streaming. Reusing the join-replay path means resume correctness (stale vehicles idle, no motion replay) inherits the already-regression-tested snapshot semantics.

**Alternatives considered**: Full `StopAsync`/dispose of the connection — rejected: renegotiation cost on every tab switch, more states to get wrong, and the group-leave already achieves zero fan-out. Server-side idle detection — rejected: client knows visibility; server guessing adds protocol. A slow `{city}:slow` group (doc R3(b)) — explicitly deferred by the spec.

---

## D6 — Wire slimming (record v2): scaled-int coords, nullable prior pair, nullable category; cache stores thinned records

**Decision**: `RouteNearestPointRecord` v2:

*The v2 field table is duplicated in data-model §1 (authoritative) and wire-slimming C1. On any conflict, data-model §1 wins.*

| Key | Field | v1 | v2 |
|---|---|---|---|
| 2 | `PriorNearestLatE5` | `double` | `int?` — `null` for already-observed vehicles |
| 3 | `PriorNearestLonE5` | `double` | `int?` — `null` for already-observed vehicles |
| 4 | `CurrentNearestLatE5` | `double` | `int` — `(int)Math.Round(lat * 1e5)` |
| 5 | `CurrentNearestLonE5` | `double` | `int` — `(int)Math.Round(lon * 1e5)` |
| 10 | `Category` | `string` (default `"bus"`) | `string?` — `null` when resolvable from the client's route catalog; non-null ONLY for the `"unknown"` data-quality signal |

Keys 0,1,6,7,8,9 unchanged. Scaling replaces `Math.Round(x, 5)` at `Worker.cs:431-434`/`464-467` (±90×10⁵ and ±180×10⁵ fit `int` comfortably; MessagePack encodes them in ≤5 bytes vs. 9). The worker sends the **full prior pair** only when it has no prior state for the vehicle (first observation — today's prior==current, `DurationMs` 0 path at `Worker.cs:461-473`) **or when the vehicle's `RouteJoinKey` changed** since its prior state (client's retained position is on another route — tweening across routes would be wrong). Client (`chef-map` JS vehicle store) animates from its retained last position when prior is `null`; no retained entry + `null` prior → snap into place (existing first-observation behavior). `Category == null` → resolve from the route catalog; non-null `"unknown"` → pass through, preserving the `ResolveCategory` fallback contract (`Worker.cs:352-356`). **The client does NOT do this today**: `vehicle-animator.js:586` is `rec.category || 'unknown'` — a per-vehicle fallback that never consults a catalog, and `map-interop.js:588` populates the catalog defaulting absent categories to `'bus'`. Both must change in Phase 3 (task T044a); see data-model §1 "Category catalog contract", which is authoritative here.

**`LastBatchCache` interplay (the doc's R2 hazard, resolved)**: `WorkerTransitHub.PublishBatch` sets the cache and fans out the *same* batch (`WorkerTransitHub.cs:24-29`) — no divergence needed. The cache upserts thinned records exactly as today (per-vehicle upsert including stale ones, `EvictAfterCycles` untouched), and replaying a `null`-prior record to a joiner is *correct by construction*: the joiner has no retained position, so it snaps the vehicle into place at `Current` — precisely the desired join behavior — while `IsStale` rides the record unchanged, so the "synthetically all moving" regression guard (`ILastBatchCache.cs:125-132`) is preserved. No cache code changes at all.

**Rationale**: ~16–20 bytes saved from coords, ~18 from the omitted prior pair, ~5–10 from category ≈ 45–50% of the ~80-byte record (FR-010/011/013, SC-004). Single mechanism (nullable field omission) covers first-seen, route-change, and join-replay self-containment (FR-012).

**Alternatives considered**: `float` instead of scaled int — rejected: float32 is only 5 bytes and ~1 m precision at these magnitudes is borderline (~7 significant digits); scaled int is exact for the already-rounded values. Delta encoding — spec Out of Scope. Splitting prior omission from int scaling into separate releases — rejected: each revision multiplies the compatibility window (doc "Deployment constraints"); one revision, one gate.

---

## D7 — Version gate: rename the join method to `JoinCityV2`; ship as one revision across both deploy lanes

**Decision**: In the same change as record v2, rename the client-join hub method constant `HubMethods.JoinCity` → `"JoinCityV2"` (server `TransitHub` + WASM client + its `Reconnected` rejoin at `SignalRNotificationService.cs:98-105`). Worker→server hop needs no gate: `SignalRHubPublisher` and the hub ship in the same container (`Program.cs:161`) and deploy atomically. Deployment order: server+worker container first, then SWA client; the MartaJazz lane (`deploy/marta-jazz`) gets the identical commit per the established wire-deploy constraint.

**Rationale (FR-015 — why a gate is required at all)**: Protocol negotiation does NOT protect against this change — both sides still speak MessagePack. Worse, MessagePack-CSharp's `ReadDouble` accepts integer encodings, so an old cached WASM client receiving v2 data would silently decode `3975390` as the literal double `3975390.0` and render vehicles at absurd coordinates — silent misrender, the exact failure FR-015 forbids. With the rename, a stale client's `JoinCity` invocation fails server-side (unknown hub method → `HubException`), the client logs the error and receives no data — an empty map until reload, which is the mandated clean failure. A new client against a not-yet-deployed old server fails symmetrically. Cost: one constant + two call sites.

**Alternatives considered**: Additive new keys (11–14) alongside deprecated 2–5 — rejected: pays both encodings during the window (negative savings) and needs a second removal release. An explicit `schemaVersion` parameter on `JoinCity` — equivalent effect, but signature-mismatch failure modes are less predictable across SignalR versions than a method-name miss; rename is the smallest fully-deterministic gate. Bumping `[Union]` keys — rejected: fails at deserialization *per message* with noisier failure semantics than failing once at join.

---

## D8 — Sequencing & verification

**Decision**: Phase 0 → 1 → 2 → 3 as ordered in plan.md; Phase 3 blocked on several days of Phase 0 baseline; R3(b)/R7 stay deferred. Each phase's verification steps are in `quickstart.md`; Phase 3 additionally re-runs the Phase 0 telemetry query to compute the SC-004 delta.

**Rationale**: SC-004/SC-005 are defined as reductions *versus the measured baseline* — without Phase 0 landing first there is nothing to compare against. Phases 1 and 2 are independent of the wire contract and of each other and can land in either order.
