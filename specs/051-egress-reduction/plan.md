# Implementation Plan: Egress Reduction at Current Scale

**Branch**: `051-egress-reduction` | **Date**: 2026-07-25 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/051-egress-reduction/spec.md` (derived from `docs/EGRESS_REDUCTION_SMALL_SCALE.md`)

## Summary

Cut outbound data-transfer cost ~60–75% at the current 500–2,000-user scale via four independently shippable phases, ordered measurement-first:

- **Phase 0 — Measure & observability**: a permanent `batch_wire_bytes` column on the existing `PerCityCycle` telemetry row (measured by serializing the exact MessagePack envelope list the worker is about to publish), a Log Analytics workspace actually wired into the Container Apps environment (today `appLogsConfiguration` resolves to `null` — logs go nowhere), and SWA Free→Standard (the Free tier's 100 GB/month cap is an outage risk at target scale).
- **Phase 1 — HTTP efficiency**: ASP.NET Core response compression (Brotli/Gzip, `EnableForHttps = true`) plus precomputed-bytes + strong-`ETag`/`Cache-Control` caching for `GetAllRouteShapes`/`GetAllRoutes`, which today re-deserialize and re-serialize every stored shape blob on every request despite a 24-hour data refresh cadence.
- **Phase 2 — Hidden-tab pause**: a Page Visibility JS interop; when `document.hidden && !IsAudioEnabled`, the client leaves its SignalR city group (new `LeaveCity` hub method) and rejoins on visibility restore — the existing `JoinCity` → `LastBatchCache.Current()` replay path provides catch-up for free. Audio-unmuted sessions are never paused (ambient background listening is preserved product behavior).
- **Phase 3 — One coordinated wire-contract revision** (`RouteNearestPointRecord` v2): coordinates as scaled `int` (×10⁵, the 5-decimal rounding already applied at `Worker.cs:431-434` made the trailing double bits pure waste), prior-position pair nullable and omitted for already-observed vehicles, `Category` nullable and omitted unless it is the `"unknown"` data-quality signal. Version-gated by renaming the join hub method (`JoinCityV2`) so mismatched peers fail cleanly at join (FR-015) instead of silently misrendering (MessagePack's `ReadDouble` happily decodes int encodings). Ships to all three MessagePack hops together and to the MartaJazz deploy branch.

R3(b) idle-downgrade and R7 unchanged-vehicle suppression are explicitly deferred (spec Out of Scope) pending Phase 0/3 measurements.

## Technical Context

**Language/Version**: C# / .NET 10.0 (all server + client projects); JavaScript ES modules (client map/audio interop); Go 1.x (`tools/telemetry-mcp` validator sync only); Bicep (infra)
**Primary Dependencies**: ASP.NET Core Minimal API + SignalR with `MessagePack.AspNetCore` (only protocol registered — no JSON fallback), Blazor WebAssembly, MessagePack-CSharp `[Key]`/`[Union]` contracts in `Shared/Events`, Parquet.Net 5.6.1 (telemetry sidecar; POCO property name IS the column name), `Microsoft.AspNetCore.ResponseCompression` (new registration, framework-included)
**Storage**: In-memory `IKeyValueRepository<string>` (route shapes, WebAPI); Azure Blob parquet (telemetry, `parquet` container, `telemetry/` virtual-dir prefix); browser local storage (client settings — read-only dependency for mute state)
**Testing**: xunit — `Shared.Tests`, `Server.WebAPI.Tests` (incl. `LastBatchCacheCrossingExclusionTests`), `Server.TransitDataWorker.Tests`, `Client.Shared.Tests`; Go table-driven tests in `tools/telemetry-mcp`; classicist style with hand-rolled fakes/spies (no mock framework). Full per-phase test matrix, layer choices, and reliability rules in [test-plan.md](test-plan.md); adds `Microsoft.AspNetCore.TestHost` to `Server.WebAPI.Tests` (Tier 1 greybox — the only escalation above component level)
**Target Platform**: Azure Container App (server+worker co-hosted, 1 replica, `1Gi`), Azure Static Web App (WASM), browsers incl. iOS Safari
**Project Type**: Web application (Blazor WASM frontend + ASP.NET Core backend + hosted worker) + Bicep IaC
**Performance Goals**: ≥35% per-vehicle wire reduction (38.7% measured; threshold amended 2026-08-01 — see spec SC-004); ≥70% route-data transfer reduction; zero live-update egress for hidden+muted sessions; ~60–75% total egress reduction vs. Phase 0 measured baseline
**Constraints**: MessagePack wire changes span 3 hops in 2 deploy lanes (worker+server atomic in one container; WASM client separate; MartaJazz ships from `deploy/marta-jazz`) — all Phase 3 field changes ship as ONE contract revision; `LastBatchCache` staleness/eviction semantics must survive unchanged (documented regression guard at `ILastBatchCache.cs`); no user-visible change to map animation, categories, or audio (spec FR-016)
**Scale/Scope**: 500–2,000 concurrent users; ~500–1,000 vehicles/city (NYMTA ~5,000); 6 publishes/min/city; ~10 production files touched per phase, no new deployables

## Constitution Check

*GATE: evaluated against Constitution v3.3.2 before Phase 0 research; re-evaluated after Phase 1 design — PASS, no violations.*

| Principle | Assessment |
|---|---|
| I. Decoupled Cloud Architecture | ✅ Unchanged. Same three deployables; no topology change (worker stays co-hosted, 1 replica). |
| II. No Frontend Secrets | ✅ Untouched. No new keys; Log Analytics shared key stays server/infra-side (Bicep `@secure()` param). |
| III. Two-Pass Pipeline | ✅ V2 pass semantics preserved — same records emitted for moved/unchanged/first-seen; only the wire *encoding* of `RouteNearestPointRecord` is thinned. V1 pass untouched. |
| IV. OpenTelemetry Observability | ✅ Strengthened — this feature *implements* the Log Analytics wiring the principle mandates, and extends the structured telemetry contract (`batch_wire_bytes`). |
| V. GitHub Actions CI/CD | ✅ Unchanged pipelines; Phase 3 must respect the two-lane deploy constraint (research D7). |
| VI. GTFS ID Mapping | ✅ `RouteJoinKey` untouched on the wire (Key(1) stays a string — it is the client's animation/audio key). |
| VII. OSM Cartography / persistent data layers | ✅ No layer or style changes. Hidden-tab pause stops *data delivery*, not layers; resume repaints from the replay snapshot. |
| VIII. Generative Music | ✅ No audio changes. Pause is gated on mute so sounding sessions are never interrupted. |
| IX–XI, XIII. UX principles | ✅ No new UI surface, copy, or CSS in this feature (pause is automatic, not a setting). Nothing to localize (XII) — no new user-facing strings. |
| Governance / tech enforcement | ✅ Azure services as specified; SWA Standard is still SWA; Log Analytics is already the mandated backend. |

**Post-design re-check**: PASS — design adds no new projects, no new resource files, no UI. Complexity Tracking not needed.

## Project Structure

### Documentation (this feature)

```text
specs/051-egress-reduction/
├── plan.md              # This file
├── research.md          # Phase 0 output — 8 decisions D1–D8
├── data-model.md        # Phase 1 output — wire record v2, telemetry column, response cache, attention state
├── quickstart.md        # Phase 1 output — per-phase verification walkthrough
├── test-plan.md         # Test hardening per util-testing gold standard — layers, doubles, reliability rules, tiers
├── contracts/
│   ├── telemetry-observability.md   # batch_wire_bytes + validator sync + bicep wiring
│   ├── http-caching.md              # compression + ETag/304 behavior for route endpoints
│   ├── visibility-pause.md          # pause/resume state machine + LeaveCity hub contract
│   └── wire-slimming.md             # RouteNearestPointRecord v2 + JoinCityV2 version gate
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
src/ChefKnifeStudios.TransitJazz.Shared/
├── Events/RouteNearestPointBatchEvent.cs     # Phase 3: record v2 (int coords, nullable prior/category)
└── CityNames.cs                              # Phase 2: HubMethods.LeaveCity; Phase 3: JoinCity → JoinCityV2

src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/
├── Worker.cs                                 # Phase 0: measure wire bytes per city; Phase 3: build v2 records (scale, thin prior/category)
└── Logging/TelemetryEvent.cs                 # Phase 0: batch_wire_bytes column

src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/
├── Program.cs                                # Phase 1: AddResponseCompression/UseResponseCompression; register response cache
├── EndpointGroups/GtfsEndpoints.cs           # Phase 1: serve precomputed bytes + ETag/304 (GetAllRouteShapes, GetAllRoutes)
├── GtfsStatic/GtfsStaticLoader.cs            # Phase 1: precompute serialized responses at load + 24h refresh
├── GtfsStatic/IRouteShapeResponseCache.cs    # Phase 1: NEW — per-city precomputed bytes + ETag
└── SignalR/TransitHub.cs                     # Phase 2: LeaveCity; Phase 3: JoinCityV2 rename

src/Client/ChefKnifeStudios.TransitJazz.Client.Core/
└── Services/SignalRNotificationService.cs    # Phase 2: PauseAsync/ResumeAsync (leave/rejoin city group); Phase 3: JoinCityV2

src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/
├── wwwroot/js/page-visibility.js             # Phase 2: NEW — Page Visibility API module (lazy-RCL idiom)
├── Services/JsInterop/IPageVisibilityJsInterop.cs  # Phase 2: NEW — interop wrapper (outside-click pattern)
├── wwwroot/js/vehicle-animator.js            # Phase 3: retained last-position fallback when prior is null; category resolution
└── wwwroot/js/map-interop.js                 # Phase 3: route-category catalog exposed to the animator (see data-model §1)

src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/
└── Pages/TransitMap.razor.cs                 # Phase 2: visibility×mute gate wiring; Phase 3: nullable prior/category passthrough

bicep/
├── main.bicep                                # Phase 0: logAnalytics module + pass IDs into cae
├── modules/logAnalytics.bicep                # Phase 0: NEW — workspace
├── modules/containerAppsEnvironment.bicep    # Phase 0: params now actually supplied (no file change needed)
└── modules/staticWebApp.bicep                # Phase 0: sku Free → Standard

tools/telemetry-mcp/internal/validate/validate.go  # Phase 0: batch_wire_bytes in kindNumeric allow-list

src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/     # Phase 1 ETag/304 + Phase 2 LeaveCity + cache-replay tests
src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/  # Phase 3 record-building tests
src/ChefKnifeStudios.TransitJazz.Shared.Tests/                   # Phase 3 round-trip serialization tests
```

**Structure Decision**: Existing web-application layout; no new projects. One new server interface + implementation (`IRouteShapeResponseCache`), one new client JS module + interop wrapper (following the established lazy-RCL-module idiom from `outside-click.js`/`TransitSynthJsInterop`), one new Bicep module. Everything else is edits to existing files.

## Implementation Phases (delivery order is binding)

| Phase | Spec story | Content | Ships independently? |
|---|---|---|---|
| 0 | US1 (P1) | `batch_wire_bytes` telemetry + validator sync; Log Analytics workspace wired to CAE; SWA Standard | Yes — pure additive |
| 1 | US2 (P2) | Response compression; precomputed route responses + ETag/`Cache-Control`/304 | Yes — HTTP only, SignalR untouched |
| 2 | US3 (P3) | Page-visibility interop; hidden×muted → `LeaveCity`; visible → `JoinCity` replay | Yes — additive hub method, old clients unaffected |
| 3 | US4 (P4) | Record v2 + `JoinCityV2` gate, all three hops + `deploy/marta-jazz`, ONE revision | Coordinated — see research D7 |

Phase 3 MUST NOT start until Phase 0 has produced **≥3 days** of per-city baseline (SC-004 is defined against it). Phase 0 must also capture the Azure Monitor SWA/Container App egress baseline **before Phase 1 deploys** — once compression ships, SC-005's pre-feature HTTP denominator is unrecoverable. Phases 1 and 2 are otherwise mutually independent.

**Design-for-test seams (binding on task generation — from [test-plan.md](test-plan.md))**: the implementation MUST expose three seams so the riskiest logic is provable at Tier 0: (1) Phase 0's serialize-and-count as a pure `WireSize.Measure(List<EventEnvelope>)` helper rather than inline tick-loop code; (2) Phase 1's `If-None-Match`/ETag decision as a pure helper the endpoints call; (3) Phase 2's pause/resume reconciliation as a plain `AttentionGate` class in **`Client.Core/Services/`** (alongside its `IDeliveryControl` implementor `SignalRNotificationService`; reachable from `Client.Shared.Tests` via the existing `Client.Shared` → `Client.Core` project reference) over an injected delivery-control interface, with the JS interop and `TransitMap` wiring kept as logic-free adapters. Each phase's Definition of Done includes its Tier 0/1 tests green per the test plan's traceability table.

## Complexity Tracking

*No constitution violations — table intentionally empty.*
