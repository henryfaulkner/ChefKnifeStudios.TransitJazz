# Phase 0 Research: Toronto TTC Transit City

All findings are grounded in the as-built code (verified during planning) and the compatibility report `docs/city-compat/ttc.md`. Each item is Decision / Rationale / Alternatives.

## R1 — Streetcar transit-mode classification (the one reality-vs-doc conflict)

**Decision**: Accept the as-built classifier. TTC streetcars (`route_type=0`) are classified `TransitMode.Rail` and voice/render on the Rail treatment for v1. No code change. Log dedicated streetcar (tram) voicing as a tracked follow-up.

**Rationale**: The compat report claims "the worker's loader classifies only `route_type=1` as Rail; everything else including `route_type=0` streetcars is treated as Bus." That is **false against the as-built code**. There is no Worker-side `route_type` classifier at all. The only classifier is the WebAPI `GtfsStaticLoader.ParseRouteMetadata` (`GtfsStaticLoader.cs:326`):

```csharp
var mode = routeType is "0" or "1" or "2" ? TransitMode.Rail : TransitMode.Bus;
```

This `TransitMode` is stored in `RouteShapeFeature`, consumed by the Worker into its per-city `_routeMode` (routeJoinKey→TransitMode) map (`Worker.cs`), stamped onto each `RouteNearestPointRecord.TransitMode` (default `Bus`, overridden to `Rail` for rail routes), and used client-side to differentiate rendering/audio. So streetcars WILL voice as Rail with zero changes. Forcing them to Bus would mean editing a classifier shared by every city (regression surface for MARTA/WMATA/MBTA/NYMTA rail) or introducing a per-city carve-out — real code and scope. User decision: ship Rail now, revisit later.

**Alternatives considered**:
- *Force `route_type=0` → Bus (globally)*: rejected — changes shared behavior; MBTA/other light-rail could regress.
- *Per-city streetcar→Bus override*: rejected for v1 — adds config surface + code path for a cosmetic gain the user chose to defer.
- *Distinct Tram `TransitMode`*: rejected for v1 — this is the "dedicated streetcar voicing" follow-up, a larger change spanning the `TransitMode` enum (a MessagePack wire contract), the classifier, and client audio.

## R2 — Route-ID alignment & transform

**Decision**: No `RouteIdNormalization`, no `RailRouteIdMap`. Empty transform config.

**Rationale**: Compat report §"Route ID alignment": RT `route_id` is a plain integer string; static `route_short_name` is the same integer string. 164/165 RT IDs match verbatim (99.4%). The existing `RouteShapeProperties.JoinKey` (`route_short_name` fallback `route_id`, per Principle VI) matches TTC as-is. The lone unmatched RT id `600` is a non-scheduled internal/special service, silently counted `skippedUnknownRoute` by existing logic. This is strictly simpler than NYMTA bus (which needed `["uppercase","plusToSbs","stripLeadingZeros"]`).

**Alternatives**: none needed — verbatim match is the ideal case.

## R3 — Rail / subway realtime

**Decision**: Omit `RailRealtime` from the TTC config entirely. No `RailRealtimeAdapter`, no subway synthesis.

**Rationale**: Compat report §Rail: TTC publishes no public live subway vehicle-position feed. Subway line **geometry** exists in the static zip (`route_type=1`, keys `1`/`2`/`4`), so subway lines could draw, but there is no live source to animate them. The `CityConfig.RailRealtime` property is nullable and only MARTA sets it; leaving it null means the Worker never attempts a rail-realtime fetch for TTC (satisfies FR-008 / SC-006). NYMTA's *subway synthesis* path is a bespoke `NymtaCity` adapter selected by name in `Program.cs`; TTC does not hit that arm, so no synthesis occurs.

**Alternatives**:
- *Draw static subway geometry only (no trains)*: this happens automatically if the static zip's `route_type=1` routes load — the P3 user story. It requires no realtime config. Whether those lines are desirable un-animated on the map is a display nicety; not blocking, not additional work.

## R4 — Keyless feeds & the static URL space

**Decision**: No `ApiKeyEnvVar`. Static zip URL must be stored URL-safe.

**Rationale**: Compat report §Auth: none for either feed. So the TTC config omits `ApiKeyEnvVar` (the `GtfsRtCity.FetchFeedAsync` code appends the key query-param only when `ApiKeyEnvVar is not null` — omitting it means a plain unauthenticated GET). The static zip URL contains a literal space: `…/download/TTC Routes and Schedules Data.zip`. In JSON config this must be percent-encoded (`%20`) or the download step must handle the space; percent-encoding in the config string is the least-surprise choice and is verified in quickstart. Note the CKAN resource ID can rotate on schedule updates — recommend mirroring/pinning as an operational follow-up (not code).

**Alternatives**: *Leave the space literal and rely on HttpClient to encode it* — risky/undefined; explicit `%20` in config is safer and testable.

## R5 — City registration mechanics (Worker, WebAPI, Client)

**Decision**: `ttc` registers via config + the existing `else` arm; add `CityNames.Ttc`; add one `CityFab` menu button with a `#ttc` hash handler.

**Rationale**: Verified in `TransitDataWorker/Program.cs`: cities are built from the `Cities:` config array; `marta` and `nymta` are special-cased, everything else (`wmata`, `mbta`, and now `ttc`) constructs a plain `GtfsRtCity(cfg, …)`. **No `Program.cs` change.** The WebAPI has its own mirrored `Cities:` array driving `GtfsStaticLoader`, so the TTC entry must be added there too for shapes to load. `CityNames.cs` currently defines marta/wmata/mbta/nymta — add `Ttc = "ttc"`. `CityFab.razor` uses inline-labelled `MatButton`s that set `location.hash` and reload; add a Toronto button + `HandleTtcClicked` mirroring `HandleMbtaClicked`.

**Localization note (Principle XII)**: `CityFab.razor` hardcodes all labels inline today (pre-existing debt). The strict path is a `RouteFilterResources.resx` `CityToronto` key via `IStringLocalizer`. Recommendation: add the new label inline to match the existing four (no new asymmetric debt), and track full-component resx migration separately. If strictness is preferred, localize all five city labels in one pass as an add-on task.

**Alternatives**:
- *A named `TorontoCity` class like `MartaCity`/`NymtaCity`*: rejected — those exist only because MARTA needs rail-realtime merge and NYMTA needs subway synthesis. TTC needs neither; the generic `GtfsRtCity` is exactly right.
- *Localize the new label via resx immediately*: viable and stricter, but produces a half-localized component; deferred to a tracked cleanup unless the user wants the full five-label pass now.

## R6 — Telemetry

**Decision**: `EmitsTelemetry: true`.

**Rationale**: TTC surface vehicles report real GPS, exactly like MARTA/WMATA/MBTA/NYMTA-bus — all of which set telemetry true. Consistent with FR-012. The existing logging sidecar (`ParquetLoggingService`) handles the new city with no change; the denormalized `telemetry` dataset just gains `ttc`-tagged rows.

**Alternatives**: none — parity with every real-GPS city.
