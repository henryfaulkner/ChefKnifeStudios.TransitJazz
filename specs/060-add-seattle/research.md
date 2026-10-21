# Seattle planning research

**Baseline**: [2026-10-18 compatibility report](../../docs/city-compat/seattle.md), [onboarding guide](../../docs/ADDING_A_TRANSIT_CITY.md), current repository seams. The five paired Sunday responses are measured evidence, not general service coverage.

## Direct sources and access

**Decision**: Use KCM keyless realtime `https://s3.amazonaws.com/kcm-alerts-realtime-prod/vehiclepositions.pb`, ST OneBusAway agency 40 realtime through the documented `key` query parameter, and only `https://www.soundtransit.org/GTFS-KCM/google_transit.zip` plus `https://www.soundtransit.org/GTFS-rail/40_gtfs.zip` for static geometry. Name the runtime variable `SOUND_TRANSIT_OBA_API_KEY`. Set no combined city-wide `ApiKeyEnvVar`; source-specific construction must append the key solely to ST realtime. No secret value appears in config or evidence.

**Rationale**: `GtfsRtCity.FetchVehiclesAsync` currently appends the same configured key to every `GtfsRtUrls` endpoint. `GtfsStaticLoader.BuildCityShapeSetAsync` also appends a city-wide key to every `StaticZipUrls` URL. Putting both realtime URLs in a generic keyed city would send the ST key to KCM, and city-wide static keying would send it to public static hosts. Both direct static files were fetched keyless in the measured report. A request carrying the private key succeeded, but keyless ST requests also succeeded, so this does not prove key validation.

**Alternatives**: The consolidated regional file is excluded even as bridge/fallback by FR-002. A general per-URL authentication framework is broader than this one city. A dedicated `SeattleCity` composed of two existing decoder instances keeps the change focused; both host registration branches must use it. Verify intended credential access separately before release.

## Route identity and static union

**Decision**: Reuse `GtfsStaticLoader`'s per-ZIP display-key merge and Worker's `BuildRouteIndex` alias from each static `RouteId` to `RouteJoinKey`, with explicit Seattle ambiguity validation. Keep the KCM ZIP first and ST ZIP second only after validation proves no display-key or raw-ID conflict. Known expected joins: `100451` to `556`, `102767` to `547`, `100479` to `1 Line`, `2LINE` to `2 Line`. Exclude `512` from map/audio because the direct agency files do not supply its route and shape.

**Rationale**: The compatibility report found no duplicate display keys or conflicting raw-route mappings in the current direct union. Current Worker already aliases `RouteId` and resolves the canonical point's `RouteJoinKey`; a hand-maintained global `RailRouteIdMap` would duplicate this information. Current static loader uses `TryAdd` on display key, and Worker `TryAdd` on raw aliases, so a changed ZIP could silently choose an arbitrary first match. Seattle must fail/withhold affected ambiguous coverage and diagnose it without replacing existing city behavior.

**Alternatives**: A hardcoded map for all observed IDs would drift on agency updates; a consolidated GTFS geometry supplement violates scope. The report's 148 geometry-bearing display keys are snapshot evidence, not a service-calendar certificate.

## Display overlap and partial source outcomes

**Decision**: Fetch the two sources independently, preserve their `CityFetchResult` observations, then make one deterministic eligible feed for Worker processing. For corroborated shared ST Express observations on mapped `556`/`547`, prefer KCM's eligible record; use ST if no possible KCM counterpart exists on the same descriptor and shared route. A same-route KCM record lacking position, time, geometry or freshness evidence remains a possible counterpart, so withhold the ambiguous pair. Match more than identifier text: shared-fleet evidence, compatible mapped route and plausible position/time are required. Source-qualify independent display vehicle state keys so unrelated equal descriptors do not collide. Withhold ambiguous matches rather than merge or double-publish them. Maintain existing timeout, cancellation and safe diagnostic behavior.

**Rationale**: Five paired snapshots had four overlapping descriptor IDs per pair, no overlapping entity IDs, and mostly near-coincident positions. Worker keys state by descriptor ID and performs route-hour movement and crossing/audio in its processing passes; merging both feeds untouched would duplicate contributions. `CityFetchResult.Combine` retains source observations; Worker's `ReportedVehicleCapture.Observe` runs before route-index and spatial processing, so the adapter can preserve raw capture while selecting display entities.

**Alternatives**: Entity-ID dedup would miss measured overlaps. Whole-route source suppression could discard legitimate ST fallback when KCM lacks a vehicle. Descriptor-only dedup might conflate unrelated vehicles.

## Reported-vehicle identity and coverage

**Decision**: Do not redesign the reported pipeline for Seattle. When a valid policy is activated, preserve per-endpoint raw receipt, success/failure/empty outcome and all nondeleted Vehicle descriptor IDs before map/geometry/overlap filters. Document two source-policy candidates with stable keys, but leave both Seattle policy arrays absent in configuration so `ReportedVehicleCatalogBuilder` excludes the unsupported city. Preserve the actual global collection/read flag values. Existing Stats retains Seattle in the configured city/hour grid and reports its unsupported reported coverage as unavailable or qualified partial in All. If later evidence proves both full endpoints share one ID namespace, the existing `IdentityNamespace` setting can be shared. Use measured service-hour receipts to choose `ExpectedCadenceSeconds` and `GapLimitSeconds`, then version/fingerprint the policy.

**Rationale**: Existing `ReportedVehicleSourceObservation` creates `(IdentityNamespace, IdentityKind, exact descriptor value)` membership per endpoint. Four verified paired vehicles mean separate namespaces would double count some physical vehicles; those pairs alone do not establish that every equal text value across endpoints means the same vehicle. A source filter for reporting would remove eligible raw observations, contrary to the existing [capture contract](../059-reported-vehicles/contracts/reported-vehicle-capture-v1.md). Unsupported coverage is truthful under FR-010 and existing Stats pending/unavailable states. Route 512 raw reports remain eligible when capture is supported, though its map/audio representation stays excluded.

**Alternatives**: Shared namespace now risks undercounting unrelated vehicles; separate namespaces now risks known duplicate counts; custom route-sensitive identities would broaden the reported contract and require separate review. No historic population is inferred or backfilled.

## Registration, statistics and release

**Decision**: Keep existing city order/index zero intact in both JSON configurations; add one `seattle`, `America/Los_Angeles`, one picker/hash entry, map origin, Stats display key/resource and existing resource-backed visitor text. Inspect effective indexed environment overrides before local and production acceptance. Existing Stats policy derives city windows from the configured time zone. Historical selectors/flags require explicit readiness; preserve their actual configured values rather than changing them merely to make Seattle appear.

**Rationale**: `Program.cs` in WebAPI and standalone Worker separately construct city registries, WebAPI has an index-zero rail reference, `StatsOverlayOptions.DisplayNameKeys` is explicit, and client city/copy/map paths are explicit. New-city registration does not inherently require a schema migration. KCM streetcar/water taxi route types currently resolve to rail/bus categories under global classification; this feature does not reclassify them.

**Alternatives**: A second Seattle agency city would fragment the requested experience and Stats identity. Automatic global history enablement could activate unsupported or unverified capture.

## Remaining release prerequisites

- ST confirmation of the permitted request rate covering actual polling intervals, retries, environments, active instances and overlapping revisions. Default 10 seconds implies 8,640 requests per endpoint/day/instance before retries, not an approved quota.
- Intended credential access and runtime secret reference verified without exposing a value; key-carrying HTTP success alone is insufficient because keyless access also succeeded.
- Both direct ZIP calendars and exception dates valid for intended service; multiple representative service-hour updates across claimed modes, including weekday coverage if claimed.
- Corroborated cross-source identity-domain policy before Seattle reported history is enabled or reported counts are claimed.
- Local build/tests, effective configuration, map/audio/Stats and existing-city checks, then human-reviewed release sequencing and production acceptance recorded in `validation.md`.
