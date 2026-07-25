# Phase 0 Research: NYC MTA Bus Support

All items below were surfaced by the design doc (`docs/nymta-bus-support-design.md`) and/or the spec's Assumptions. Each resolves to a **Decision** grounded in the as-built code that was read during planning.

---

## R1 — Reuse `GtfsRtCity`, not a new adapter or a merge into `NymtaCity`

**Decision**: Register `nymta-bus` as an ordinary `GtfsRtCity` config entry; do **not** merge it into `NymtaCity` and do **not** create a new `ITransitCity`.

**Rationale**: Confirmed against `Program.cs:43-57` — the city-registry factory has an explicit `else` arm (`cities.Add(new GtfsRtCity(cfg, httpFactory, logFactory.CreateLogger<GtfsRtCity>()))`) that any config entry whose name isn't `marta`/`nymta` falls into. `wmata` and `mbta` already take exactly this path. `nymta-bus` needs **zero** `Program.cs` change. `NymtaCity` exists specifically for subway *synthesis* (offset table, shape interpolation); bolting real-GPS bus fetch onto it would give it a second unrelated responsibility for no benefit.

**Alternatives considered**: (a) One merged "nymta" city holding bus+subway — rejected: subway is `EmitsTelemetry:false` (synthesized) and bus is `true` (real GPS); one entry can't hold both, and `GtfsStaticLoader` would have to special-case which zips feed the offset builder vs. the route index. (b) A bespoke `NymtaBusCity` — rejected: nothing bespoke is needed; it's a vanilla GTFS-RT feed.

---

## R2 — `RouteIdNormalizer`: an ordered transform pipeline, not a static map

**Decision**: New pure static class `RouteIdNormalizer` in `TransitDataWorker/Cities/`, exposing `Apply(string routeId, IReadOnlyList<string> steps)` that folds an ordered list of **named** steps over the route ID. Steps for v1: `uppercase`, `plusToSbs` (`"M15+"` → `"M15-SBS"`), `stripLeadingZeros` (`"Q06"` → `"Q6"`, regex `^([A-Z]+)0*(\d.*)$`). Unknown step name = no-op passthrough (never throws).

**Rationale**: `CityConfig.RailRouteIdMap` (read at `CityConfig.cs:9`) is a `Dictionary<string,string>` — a fixed pair lookup WMATA uses for 12 rail entries. NYC bus's fixes are *transforms* over ~266 IDs (case-fold applies to all; `+`→`-SBS` is a suffix rule; zero-strip is regex-shaped), which a static dict structurally cannot express without enumerating every input. A pure function keeps `GtfsRtCity` a thin orchestrator and makes the logic unit-testable with no HTTP/host.

**Ordering**: `uppercase` → `plusToSbs` → `stripLeadingZeros` — the sequence the feed evaluation measured to 100% match. `stripLeadingZeros`'s regex assumes an uppercase letter prefix, so `uppercase` must precede it. Order is fixed by config array order; `Apply` honors it.

**Alternatives considered**: Extending `RailRouteIdMap` with wildcard/regex values — rejected: overloads a simple lookup with a parser and risks changing WMATA behavior. A per-city `Func<string,string>` in code — rejected: not config-driven, not declaratively testable, and couples the transform set to a code deploy.

---

## R3 — Invocation seam inside `GtfsRtCity`

**Decision**: Add `ApplyRouteIdNormalization(merged)` called immediately after the existing `ApplyRailRouteIdMap(merged)` at `GtfsRtCity.cs:37`, iterating `feed.Entities` and rewriting `entity.Vehicle.Trip.RouteId` in place when `config.RouteIdNormalization is { Length: > 0 }`.

**Rationale**: This is the exact seam `ApplyRailRouteIdMap` already uses (`GtfsRtCity.cs:60-70`) — same iteration shape, same null-guard (`entity.Vehicle?.Trip?.RouteId is not null`), applied before the `FeedMessage` returns to `Worker.cs`. Running normalization *after* the map means a future city could use both (static remap then transform); NYC bus only sets the transform pipeline, so `RailRouteIdMap` is a no-op for it (its dict is null → early return). Guarding on `Length > 0` means every existing city (empty/absent `RouteIdNormalization`) does zero extra work — no behavior change (satisfies FR-006, FR-014, SC-004).

---

## R4 — RT feed credential query-param name (`?key=` vs `?api_key=`) — the one real friction

**Context**: `GtfsRtCity.FetchFeedAsync` (`GtfsRtCity.cs:44`) hardcodes `requestUrl = apiKey is not null ? $"{url}?api_key={apiKey}" : url`. The obanyc bus feed expects `?key=<KEY>`, per the feed evaluation's manual curl. So `ApiKeyEnvVar` + the current hardcoded `?api_key=` would build the wrong URL.

**Decision**: **Pre-template the credential in the `GtfsRtUrls` value and leave `ApiKeyEnvVar` unset** for `nymta-bus`, BUT keep the actual key OUT of committed config — the committed `appsettings.json` uses a config-substitution placeholder for the key, resolved from environment at runtime. Concretely: the `GtfsRtUrls` entry is `"https://gtfsrt.prod.obanyc.com/vehiclePositions?key=${NYMTA_BUS_API_KEY}"` in committed config, and the value is materialized from the `NYMTA_BUS_API_KEY` environment variable at startup (via the existing env-var/config layering the Worker already uses). This ships **zero code change to `GtfsRtCity`** while honoring "no committed secrets" (constitution Principle II spirit — no server secret in the bundle/repo).

**Fallback / to-confirm before shipping (tasks note)**: If the Worker's config layering does **not** perform `${VAR}` substitution inside a `GtfsRtUrls` string out of the box (ASP.NET config does not expand `${}` by default), the minimal correct fix is a tiny **per-city configurable query-param name** on `CityConfig` (e.g. `ApiKeyQueryParam` defaulting to `"api_key"`, set to `"key"` for `nymta-bus`) and one line in `FetchFeedAsync` using it. This is ~3 lines, keeps `ApiKeyEnvVar`/env-var secrecy, and is the recommended path if substitution isn't already wired. **Task T-config MUST verify which mechanism the running Worker actually supports and pick accordingly — this is the single "confirm before shipping" item from the spec.**

**Rationale**: Prefer the zero-code path *only if* it keeps the key in an env var, not committed. The design doc's literal "just put `?key=<KEY>` in the URL" is rejected as-written because it would commit a live key. The `ApiKeyQueryParam` fallback is the clean code-based alternative and is cheap.

**Alternatives considered**: Changing the hardcoded `?api_key=` globally to `?key=` — rejected: would break WMATA (uses `api_key`). Per-city param name is the surgical version of this.

---

## R5 — Frontend: one new picker entry (Option A), localized label

**Decision**: Add a single "New York Buses" button to `CityFab.razor` navigating to `#nymta-bus` (mirroring the existing `HandleNymtaClicked` → `location.hash='nymta';location.reload()` pattern at `CityFab.razor:54-57`). Its label MUST be a resx string (`CityNymtaBus`) via `IStringLocalizer<RouteFilterResources>`, per Principle XII.

**Rationale**: Confirmed the client is one-hash-one-city — `CityFab` just sets the hash and reloads; the map subscribes to whichever city name is in the URL. A second backend city needs only a second button. Option A (separate "New York Subway" + "New York Buses" views) is zero new client plumbing; Option B (unified NYC map joining two SignalR groups) is explicitly out of scope (spec Assumptions).

**Localization scope**: The *existing* four buttons hardcode `Label="Atlanta, GA"` etc. inline — pre-existing Principle XII debt, **not** introduced by this feature. This feature's required scope is the **one new** `CityNymtaBus` resx key. Converting the four legacy inline labels is a nice-to-have noted as out-of-required-scope so this feature doesn't silently balloon. (The existing "New York, NY" button stays pointed at `#nymta` = subway; it is not renamed here — renaming it to "New York Subway" for symmetry is an optional polish task.)

---

## R6 — Static data: all 5 NYCT borough zips + MTA Bus Company zip (6 URLs)

**Decision**: List all six zips in `nymta-bus`'s `StaticZipUrls`. No code change — `GtfsStaticLoader.BuildCityShapeSetAsync` already merges multiple zips per city via `TryAdd` across `allRouteToShape`/`allShapes`/`allMeta` (precedent: WMATA lists 2 zips today, `appsettings.json:19-22`).

**Rationale**: `routes.txt` is byte-identical across the 5 borough zips (the route registry only needs one), but each borough's `trips.txt`/`shapes.txt` carries distinct shapes — listing all 5 avoids partial route-shape coverage at zero code cost. The MTA Bus Company zip is genuinely additive (Q06–Q115 locals, QM/BXM express not in NYCT zips), so it must be included for FR-008. Per-zip failures are already tolerated by the loader's existing try/catch (FR-010).

**Alternatives considered**: The feed-eval minimum (1 borough zip + Bus Co, 2 URLs) — reaches route-match 100% but risks missing borough shapes; rejected for v1 in favor of full coverage since the cost is only 4 extra URL strings.

---

## R7 — Telemetry `true`

**Decision**: `EmitsTelemetry: true` for `nymta-bus`.

**Rationale**: Bus positions are real live GPS, structurally identical to MARTA/WMATA/MBTA bus, all of which are `EmitsTelemetry: true` (`appsettings.json`). Subway is `false` only because its positions are *synthesized*. Excluding real bus GPS would be the sole telemetry gap among bus-equipped cities (FR-013).

---

## R8 — Testing approach

**Decision**: New xUnit `RouteIdNormalizerTests` in the existing `...TransitDataWorker.Tests` project, using `[Theory]`/`[InlineData]` accept vectors. Pure function → no host, no HTTP, no mocks (matches existing `TriggerPointEqualityTests`/`CrossingDetectorTests` style).

**Vectors** (from contract): `Apply("bx3", ["uppercase"]) == "BX3"`; `Apply("M15+", ["plusToSbs"]) == "M15-SBS"`; `Apply("Q06", ["stripLeadingZeros"]) == "Q6"`; `Apply("Q06", full) == "Q6"`; `Apply("M15+", full) == "M15-SBS"`; `Apply("bx3", full)`; unknown-step no-op; empty-steps passthrough; no-digit/no-prefix unchanged (`"S"`, `"SBS"`). See `contracts/route-id-normalizer.md`.

---

## Open items carried to tasks

1. **R4 credential mechanism** — confirm whether committed-config `${NYMTA_BUS_API_KEY}` substitution works in the Worker's config layering; if not, add `CityConfig.ApiKeyQueryParam` (~3 lines). *This is the only pre-ship verification.*
2. **Obtain `NYMTA_BUS_API_KEY`** — operational prerequisite (register for an obanyc key), not code. Buses won't appear until it's set in the Worker's environment.
