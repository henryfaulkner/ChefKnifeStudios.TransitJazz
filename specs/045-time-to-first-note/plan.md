# Implementation Plan: Time-to-First-Note

**Branch**: `045-time-to-first-note` | **Date**: 2026-07-20 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/045-time-to-first-note/spec.md`

## Summary

Close the ~10–15 s silent gap between unlocking audio and the first audible note. The diagnosis (`docs/TIME_TO_FIRST_NOTE_DISCOVERY_DOCUMENT.md`) attributes it to two compounding causes: (1) the app produces **no sound at all** at unlock — the ambient noise bed and the per-route samplers are built lazily on the first crossing — and (2) crossing supply is ~34× below geometric expectation and arrives in ~30 s bursts, dominated by reverse-direction vehicles that can **never** emit a crossing because both travel directions of a route share the single shape stored under its one `RouteJoinKey` (= `route_short_name`, fallback `route_id`, per constitution VI v3.3.2 — NOT `route_id`).

The plan ships the fixes in the spec's priority order, each independently deployable, each with a falsifiable numeric forecast (FR-015):

- **US1 (P1, client-only)** — build the master bus + start the noise bed inside `unlock()`, and warm the 3 prod samplers at unlock, so there is audible output at t=0 and the first crossing plays instantly. Add a permanent `[TTFN]` probe (US5) at the same time so the change is measurable.
- **US2 (P1, worker-only)** — instrument the four `CrossingDetector` suppression paths into `PerCityCycle` telemetry (before fixing, so the fix is verifiable), then fix reverse-direction muteness so the ~half of the fleet travelling against the stored shape can emit crossings.
- **US3 (P2, server-only)** — replay recent, age-capped crossings on `JoinCity` so a fast unlock isn't guaranteed silent, without reintroducing the "rapid pulsing" regression.
- **US4 (P2, client-only)** — attach the unlock click listener before the Tone import completes so a slow-load click still runs inside the gesture-trust window (iOS permanent-silence edge case).
- **US5 (P3)** — the `[TTFN]` probe (folded into US1) plus a telemetry-only musical-density health check.

All work is confined to the TransitDataWorker (server), the WebAPI SignalR hub/cache (server), and the Client.Shared JS/interop (client). No wire-contract break: `RouteCrossingBatchEvent` is already `[Union(1)]` in the MessagePack contract, so the US3 replay is server-only. The only wire-adjacent change is new **telemetry** columns (US2), which requires a matching update to the Go allow-list validator (`tools/telemetry-mcp/internal/validate/validate.go`) — FR-016.

## Technical Context

**Language/Version**: C# / .NET 10.0 (Worker, WebAPI, Shared); JavaScript ES modules (Client.Shared `wwwroot/js`); Go 1.x (telemetry-mcp validator — allow-list edit only)
**Primary Dependencies**: Tone.js 15 (esm.sh), MapLibre GL JS, SignalR + MessagePack (`[Union]` wire contract), Parquet.Net 5.6.1 (telemetry sidecar), FluidR3 GM soundfonts (gleitz.github.io CDN)
**Storage**: In-memory only for this feature — `LastBatchCache` (WebAPI singleton), Worker `_crossingBaselines` / `_vehicleStates` `Dictionary`s; telemetry parquet in Azure Blob (existing sidecar, schema extended)
**Testing**: xUnit (`*.Tests` projects — `CrossingDetectorTests`, `LastBatchCacheCrossingExclusionTests`, `TelemetryEventSchemaTests`); Go `go test` for the validator; manual browser benchmark harness (§5 of the discovery doc) for TTFN
**Target Platform**: Blazor WASM (browser, incl. iOS Safari) + Linux container (Worker/WebAPI on Azure Container Apps)
**Project Type**: Decoupled cloud app — Blazor WASM frontend + ASP.NET Core WebAPI (SignalR hub) + TransitDataWorker background service (Constitution Principle I)
**Performance Goals**: Audible output <1 s after unlock (SC-001); ≥2× crossings/cycle for MARTA evening baseline (SC-002); dwell time-to-first-note median <5 s (SC-003); first-note trigger→audible ≈0 ms once warmed (SC-005)
**Constraints**: Sampler RAM must stay flat — warm only the 3 fixed PROD_INSTRUMENTS slots (the 1.2–1.7 GB regression lever, `transit-synth.js` header); noise bed must honor persisted mute (`_audioEnabled`); replayed crossings age-capped to avoid rapid-pulsing; NYMTA batch must stay under the feature-040 5 MB SignalR ceiling; telemetry columns are a frozen snake_case contract shared with the Go validator
**Scale/Scope**: 5 cities (MARTA primary), up to ~2,000 vehicles/tick (NYMTA); 10 s Worker tick; ≤6 resident samplers (3 in prod)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Relevance | Verdict |
|-----------|-----------|---------|
| I. Decoupled Cloud Architecture | Changes touch Worker, WebAPI, Client independently; each user story is deployable on its own | ✅ Respected — no new coupling; US1/US4 client-only, US2 worker-only, US3 server-only |
| III. Two-Pass Pipeline | Crossing detection rides the V2 pass; reverse-direction fix and counters live inside the V2 reconciliation loop | ✅ No change to the two-pass structure or `RouteNearestPointBatchEvent`/`RouteJoinKey` semantics |
| IV. OpenTelemetry Observability | New suppression counters are structured per-cycle metrics; logged and emitted as telemetry | ✅ Extends existing per-cycle logging (`moved/unchanged/skipped…`) |
| VI. GTFS ID Mapping (`RouteJoinKey`) | Reverse-direction fix must not alter join-key semantics; crossings still stamped with resolved `RouteJoinKey` | ✅ Fix operates on along-distance direction, not the join key. Per constitution v3.3.2, `RouteJoinKey` = `route_short_name` (fallback `route_id`), NOT `route_id` — both travel directions of a route collapse to that ONE key and share the ONE stored shape, which is precisely why reverse-direction vehicles have monotonically decreasing along-distance and can never emit today. Validate this single-shape-per-key premise against the short-name keying (`Worker.cs` `BuildRouteIndex`) before implementing, do not assume it |
| VII. OSM Cartography / data layers persist | Noise-bed + sampler warming is audio-only; no map layer or basemap change | ✅ Out of scope of map layers |
| VIII. Generative Transit Music (deterministic, non-authored) | Warming builds the SAME deterministic samplers earlier; a welcome motif (if added) must be data-derived, not hand-composed; crossing→note mapping unchanged | ✅ No per-route authoring introduced; warming changes *when*, not *what* |
| XI. Snappy, Reversible Overlays | Unlock is the overlay dismissal; must stay instant | ✅ Warming runs async after the gesture; does not delay dismissal |
| XII. Settings-Driven (Audio mute) | Noise bed + welcome + warming MUST obey the persisted Audio mute setting | ✅ Gated on `_audioEnabled` exactly as `getMasterBus` already does (FR-002) |
| Localization (single `.resx`) | If a visible "warming…" or welcome string is added it must come from `RouteFilterResources.resx` | ✅ Plan adds no new user-visible copy by default (audio-only); any string routes through the resx |
| Telemetry snake_case frozen contract (013/014) | New suppression columns extend `TelemetryEvent` and MUST update the Go allow-list | ⚠️ Tracked as FR-016 — coordinated edit to `validate.go` + `TelemetryEventSchemaTests`; not a violation, a required paired change |
| Wire contract (MessagePack `[Union]`) | US3 replays `RouteCrossingBatchEvent` which is already `[Union(1)]` | ✅ Server-only; no 3-lane wire deploy (per `project_signalr_wire_deploy_constraint` memory) |

**No violations requiring Complexity Tracking.** The single flagged item (telemetry schema extension) is an intentional, contract-respecting paired change, not an unjustified complexity.

## Project Structure

### Documentation (this feature)

```text
specs/045-time-to-first-note/
├── plan.md              # This file
├── research.md          # Phase 0 — 8 decisions (warming, noise bed, direction fix, replay, unlock, probe, counters, spacing-deferred)
├── data-model.md        # Phase 1 — TelemetryEvent additions, CrossingBaseline direction state, replay snapshot, TTFN probe object
├── quickstart.md        # Phase 1 — per-story manual verification incl. §5 benchmark protocol
├── contracts/           # Phase 1 — telemetry-schema.md, crossing-suppression-counters.md, join-replay.md, ttfn-probe.md, unlock-warming.md
└── tasks.md             # Phase 2 (/speckit-tasks — NOT created here)
```

### Source Code (repository root — real paths)

```text
src/
├── ChefKnifeStudios.TransitJazz.Shared/
│   └── Services/TriggerPointGenerator.cs        # spacing constant (US-spacing, DEFERRED — comment/constant only if touched)
│
├── Server/
│   ├── ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/
│   │   ├── Worker.cs                            # US2: wire suppression counters through the V2 loop + CityTickResult + PerCityCycle row
│   │   ├── Checkpoints/CrossingDetector.cs      # US2: return per-reason suppression outcome; reverse-direction emission
│   │   └── Logging/TelemetryEvent.cs            # US2: new snake_case suppression-count columns
│   │
│   └── ChefKnifeStudios.TransitJazz.Server.WebAPI/
│       └── SignalR/
│           ├── ILastBatchCache.cs               # US3: cache recent crossings alongside the position snapshot
│           └── TransitHub.cs                    # US3: replay age-capped crossings on JoinCity
│
├── Client/ChefKnifeStudios.TransitJazz.Client.Shared/
│   ├── wwwroot/js/transit-synth.js              # US1: build master bus + noise bed in unlock(); warmProdSamplers(); US4: attach listener pre-import; US5: [TTFN] probe
│   └── Services/JsInterop/TransitSynthJsInterop.cs  # US1: WarmSamplersAsync() interop wrapper (if driven from C#)
│
tools/telemetry-mcp/internal/validate/
├── validate.go                                  # US2: add new suppression columns to the frozen allow-list (FR-016)
└── validate_test.go                             # US2: accept/reject vectors for new columns

# Tests
src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests/
├── CrossingDetectorTests.cs                     # US2: reverse-direction emission + per-reason counts
└── TelemetryEventSchemaTests.cs                 # US2: new columns present + round-trip
src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests/
└── LastBatchCacheCrossingExclusionTests.cs      # US3: exclusion → age-capped inclusion (rewrite the guard the test pins)
```

**Structure Decision**: Existing decoupled solution (Constitution §Tech Stack). No new projects. Each user story maps to one owning tier — US1/US4/US5-probe → `Client.Shared`; US2/US5-health → `TransitDataWorker` (+ Go validator); US3 → `WebAPI` SignalR. This is what makes the stories independently shippable.

## Phasing & Sequencing

The spec's priorities also encode a deliberate **measure-before-fix** order (FR-015):

1. **US1 + US5 probe together (P1, client)** — ships immediate audible feedback AND the `[TTFN]` measurement in one deploy. The probe must exist before/with the fix so the forecast (`trigger→audible → ~0 ms`; audible at t=0) is verifiable. Standalone MVP.
2. **US2 counters first, then US2 direction fix (P1, worker)** — instrument the four suppression paths, deploy, read the attribution from `PerCityCycle`, *then* ship the reverse-direction fix and verify tones/tick ≥2× (SC-002). Do not stack the fix on top of the counters in one deploy — the counters' whole purpose is to confirm which path dominates.
3. **US3 (P2, server)** — replay recent crossings; verify fast-click TTFN converges to dwell (SC-004).
4. **US4 (P2, client)** — unlock robustness; verify no permanent-silence in a throttled/iOS trial (SC-006).
5. **Spacing (DEFERRED, out of scope)** — only revisit `TriggerPointGenerator` spacing after #2–#3 are measured (Assumptions; discovery §4 #5). Not in this feature's task set.

Each numbered step is its own deploy with its own §4.1 forecast checked against a recorded `[TTFN]`/telemetry row before the next is stacked.

## Complexity Tracking

*No constitution violations requiring justification.* The telemetry-schema extension is a contract-respecting paired change (FR-016), documented in `contracts/telemetry-schema.md`, not added complexity.
