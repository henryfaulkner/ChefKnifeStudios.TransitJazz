# Implementation Plan: Add Seattle Transit

**Branch**: `060-add-seattle` at task-generation handoff; initial planning ran on `059-reported-vehicles`. Agents performed no branch changes. | **Date**: 2026-10-18 | **Spec**: [spec.md](spec.md)
**Input**: [Seattle specification](spec.md), [compatibility evidence](../../docs/city-compat/seattle.md), and [city onboarding guide](../../docs/ADDING_A_TRANSIT_CITY.md). Planning only.

## Summary

Add one `seattle` city backed only by direct King County Metro (KCM) and Sound Transit (ST) realtime and static feeds. A focused Seattle adapter fetches with separate source access, applies `SOUND_TRANSIT_OBA_API_KEY` only to the ST agency-40 realtime request, preserves the raw capture boundary for any later valid reported policy, and selects one map/history/audio observation for verified overlaps. Existing direct-static `route_id` aliases resolve the measured source IDs to display `RouteJoinKey`; reject ambiguous mappings and omit route 512 from map/audio until durable direct-agency geometry exists. Register Seattle in both hosts and visitor/Stats surfaces while preserving actual global history flag values.

## Technical Context

**Language/Version**: C#/.NET 10, Razor/Blazor WASM, JSON configuration, `.resx`.  
**Primary Dependencies**: Existing protobuf-net GTFS-RT model, `HttpClient`, `TimeProvider`, static GTFS ZIP loader, Worker, Stats and xUnit projects; no new external dependency.  
**Storage**: Existing route/category/reported history only; no Seattle-specific table or migration planned.  
**Testing**: Existing Worker, WebAPI, Shared and Client.Shared xUnit suites; solution build; sanitized adapter/config fixtures; later browser/listening and production checks. [Traceable verification design](testing.md).  
**Target Platform**: Existing WebAPI-hosted worker and standalone Worker, Azure backend, Blazor WASM client.  
**Project Type**: Existing multi-city realtime web application.  
**Performance Goals**: Preserve the default 10-second worker cycle without extra per-cycle calls; confirm ST permitted rate against actual instances, environments and retries before release.  
**Constraints**: Direct agency sources only; per-source authentication; raw receipt/capture before route and overlap filters; one published/history/audio vehicle per verified overlap; no fabricated 512 geometry; existing Stats semantics; no secret values in files/logs.  
**Scale/Scope**: One city, two realtime endpoints, two direct static ZIPs, one ST credential name. Measured Sunday snapshots found 42 KCM and 5 ST route IDs, four paired overlapping descriptors and unmatched 512; they do not establish weekday coverage.

## Constitution Check

Constitution v3.3.2 evaluated before research and after design. Planning gates pass; runtime and release evidence remain pending.

| Principle | Initial / post-design | Decision |
| --- | --- | --- |
| I-II architecture and secrets | Pass / Pass | Existing hosts; only ST server request reads named environment variable. Static and KCM requests remain keyless; client receives no credential. |
| III, VI pipeline and route identity | Pass / Pass | Existing two-pass Worker; source IDs resolve to `RouteJoinKey` through validated direct-static aliases. Raw reported extraction precedes display selection. |
| IV-V observability and delivery | Pass / Pass | Bounded source outcomes and safe endpoint identities; existing CI and human-controlled publication. |
| VII-XI map, music, selection, overlays | Pass / Pass | One city uses existing map/audio and persistent route filtering; 512 is not displayed or sonified. |
| XII resources | Pass / Pass | New visitor strings use existing `RouteFilterResources.resx` and current English resource practice. |
| XIII themes | Pass / Pass | Reuse existing components/palette and verify both themes. |
| Repository Git rule | Pass / Pass | Optional `before_plan`/`after_plan` commit hooks skipped; no commit, branch change or deployment. |

## Project Structure

### Documentation (this feature)

```text
specs/060-add-seattle/
  spec.md; checklists/requirements.md
  plan.md; research.md; data-model.md; testing.md; quickstart.md; validation.md
  contracts/seattle-source-and-identity-v1.md
  contracts/seattle-visitor-and-release-v1.md
  tasks.md                         # later speckit-tasks output, not created here
```

### Source Code (repository root)

```text
src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/
  Cities/{CityConfig,GtfsRtCity,SeattleCity}.cs
  Statistics/{ReportedVehicleCatalogBuilder,ReportedVehicleSourceObservation}.cs # inspect existing contract; change only if separately justified
  Worker.cs / Program.cs / appsettings.json
src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/
  GtfsStatic/GtfsStaticLoader.cs
  Statistics/StatsOverlayOptions.cs
  Program.cs / appsettings.json
src/ChefKnifeStudios.TransitJazz.Shared/CityNames.cs
src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/
  Components/{AudioUnlockOverlay,FABs/CityFab,FABs/InfoFab}.razor
  Resources/RouteFilterResources.resx
  existing city map-origin configuration
existing Worker, WebAPI, Shared and Client.Shared test projects
```

**Structure Decision**: Keep city and Worker boundaries. A Seattle-specific composition owns two realtime source configs, authentication and deterministic overlap selection. Static loading remains keyless for Seattle. Reuse the existing reported source/namespace contract; source data has not yet justified a shared namespace for the entire endpoints. No general multi-agency framework, reported pipeline redesign or new persistence schema.

## Design

1. **Requests and route catalog**: Configure KCM S3 realtime keyless; ST OneBusAway agency 40 with query parameter `key` and environment variable `SOUND_TRANSIT_OBA_API_KEY`; both direct static ZIPs keyless. Compose two source-specific GTFS fetches under `SeattleCity`; never set a city-wide `ApiKeyEnvVar` on the combined config. Register identically in both hosts. Existing static loader merges by display key and Worker aliases static `route_id`; add Seattle validation for duplicate display keys, conflicting raw-ID aliases and known joins. Ambiguous routes lose coverage with diagnostics. 512 stays outside map/audio; never load consolidated GTFS.
2. **Capture then display selection**: When Seattle reported capture is supported, preserve each decoded source's receipt UTC, success/failure/successful-empty outcome, descriptor IDs and integrity in `CityFetchResult.ReportedVehicleObservations` before route mapping, geometry, freshness or overlap checks. Combine observations unchanged. For display/history/audio, treat matching descriptor IDs on mapped shared ST Express routes `556`/`547` as an overlap only when timestamp difference is at most 120 seconds and position difference is at most 5 km. Prefer eligible KCM for a corroborated pair; use ST when no possible KCM counterpart exists on the same descriptor and shared route. If a KCM same-route record exists but lacks eligibility/comparison evidence, or the evidence exceeds either bound, withhold the ambiguous pair and emit a bounded reason. Source-qualify display state for independent vehicles with equal text. Selection precedes Worker processing, so route-hour history and both processing passes see one representation. Confirm thresholds against broader service-hour fixtures before release.
3. **Reported identity policy**: Keep `(namespace, kind, exact descriptor ID)` memberships and independent source coverage. Document stable candidate source keys and descriptor rules for both endpoints, but leave Seattle's configured reported policies absent until a fleet-wide identity-domain contract is established. `ReportedVehicleCatalogBuilder` then treats Seattle as unsupported without blocking an already-enabled global collector; existing Stats read policy keeps Seattle in the city/hour grid and returns unavailable/partial qualification rather than a fabricated zero. Four paired overlaps prove that blindly using separate namespaces overcounts; they do not prove all equal IDs across both endpoints are one vehicle, so blindly sharing a namespace can undercount. Source selection would discard eligible raw IDs and violate FR-008/009. Preserve all existing global flag values; do not activate Seattle reported capture or claim counts while this condition remains. If later evidence establishes a common domain, use the existing shared namespace configuration and measured cadence/gaps; if it establishes only a route subset, make that a separately reviewed scope change. Keep route 512 and duplicate raw observations eligible under the original extractor when a valid policy is activated, independent of display selection.
4. **Registration and experience**: Add `CityNames.Seattle`, same effective city/source/static/time-zone config in both hosts, preserve existing city indices, and add intended historical selectors only with ownership/flag checks. Add one picker/hash entry, Seattle map origin in `Client.WebApp/Pages/TransitMap.razor.cs`, Stats key/resource, authored three-paragraph unlock and InfoFab copy with attribution and 512 limitation. Describe observed modes conservatively and account for current streetcar/water-taxi categories. Preserve actual global reported, route-hour, category and Stats read flag values.
5. **Evidence and rollout**: Use `validation.md` to separate the measured 2026-10-18 compatibility baseline from unperformed checks. Confirm ST quota/rate, credential access, direct-static calendars/exceptions, representative service-hour coverage and identity-domain policy before release. Verify effective overrides, one capture owner, schema gates and backend-first publication. Human controls commits/release; production acceptance requires running versions and visitor evidence.

## Phase 0: Research Results

[research.md](research.md) records decisions, code seams, measured evidence, alternatives and release prerequisites. No product-scope clarification remains. Key acceptance, quota, calendar validity and broad service-hour coverage are explicitly unverified.

## Phase 1: Design Outputs

[data-model.md](data-model.md), [source/identity contract](contracts/seattle-source-and-identity-v1.md), [visitor/release contract](contracts/seattle-visitor-and-release-v1.md), [testing.md](testing.md), [quickstart.md](quickstart.md) and [validation.md](validation.md). Update only the SPECKIT plan reference in `AGENTS.md`. Re-evaluate constitution as above.

## Phase 2: Task Handoff

Ready for `speckit-tasks` after review. Order: source access/route validation; raw capture/identity and overlap adapter; both host registrations; visitor/Stats resources; focused fixtures/build; local service-hour/browser/listening acceptance; release prerequisites and production validation. Planning creates no `tasks.md` or runtime code.
