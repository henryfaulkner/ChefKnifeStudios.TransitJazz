# Incident Report: Denver Producing No Tones

**Incident date:** 2026-08-29 to present

**Investigation date:** 2026-08-30

**Environment:** Production (`deployment_environment_name=prod`)

**Affected service:** `marta-jazz-dev-ca-server` / `transitjazz-transit-worker`

**Affected city:** Denver (`transit_city=denver`)

**Status:** Investigation ongoing

## Executive summary

Denver is currently fetching GTFS-RT input and completing worker cycles, but it produces no tones. The current failure is not an active route-index outage: the latest worker revision has a populated Denver route index and trigger-point cache.

The strongest current conclusion is that Denver feed entities are being discarded before successful spatial reconciliation. Grafana does not currently expose the detailed rejection counters needed to distinguish missing position data, missing route IDs, unknown route IDs, or failed spatial snapping.

A prior revision and the current revision history show two related but distinct conditions:

1. Revision `0000154` processed Denver vehicles and emitted tones.
2. Around Aug 29 at approximately 09:03 EDT, revisions `0000155` and `0000156` entered a route-index-unavailable state.
3. Later revisions restored the route index, but Denver continued to process zero vehicles and emit zero tones.

Therefore, a server restart or revision replacement explains the temporary route-index gap, but it does not explain the continuing Denver outage by itself.

## Customer impact

- Denver has no backend tone output during the current incident window.
- The worker remains live and continues cycling approximately every few seconds.
- Denver's feed fetch continues to succeed and returns nonzero records.
- Other cities continue to process vehicles, demonstrating that the worker and Prometheus pipeline are not generally stalled.

## Timeline

All times below are Eastern Daylight Time unless noted otherwise.

| Time | Revision / signal | Interpretation |
|---|---|---|
| Before Aug 29, approximately 09:03 AM | Revision `0000154` | Denver was functioning; Grafana recorded as many as 268 vehicles processed and 99 tones in sampled cycles. |
| Aug 29, approximately 09:03 AM | Revision `0000155` appears; route index briefly reports `0`. | A deployment or restart is consistent with a startup route-index gap. |
| Aug 29 onward | Revision `0000156` | Denver continues receiving input, but route index remains unavailable in the observed series and no vehicles or tones are processed. |
| Aug 30, approximately 06:58 AM | Revision `0000158` | Route index returns to `139`, but vehicle processing and tone output remain `0`. |
| Aug 30, approximately 07:08 AM | Revisions `0000161` and `0000162` | Route index remains populated, while Denver continues to process zero vehicles. |
| Aug 30, approximately 07:56 AM | Current instant snapshot | Revision `0000162` is cycling, fetching input, and receiving approximately 70 valid records, but emits zero tones. |

## Grafana evidence

Source dashboard: [TransitJazz Worker Overview](https://gallantpuffin3113.grafana.net/d/transitjazz-worker-overview/transitjazz-worker-overview)

Dashboard UID: `transitjazz-worker-overview`

Prometheus datasource: `grafanacloud-prom`

### Current Denver snapshot

The current active series is revision `0000162`:

| Signal | Current value | Meaning |
|---|---:|---|
| Worker/city health | `1` | The worker is cycling without a reported city error. |
| Input fetch success | `1` | The RTD source is responding successfully. |
| Input timestamp-known ratio | `1` | Current records have timestamps. |
| Input lag | approximately 25 seconds | The feed is recent. |
| Input records valid | approximately 70 | Nonzero feed entities are arriving. |
| Route index | `139` | Denver route geometry is loaded. |
| Route trigger-point cache | `139` | Trigger points are loaded for the indexed routes. |
| Vehicles processed | `0` | No entity reached the successful reconciliation count. |
| Tones emitted | `0` | No crossings reached tone output. |
| Vehicle state cache | `0` | No vehicle was retained after successful snapping. |
| Crossing baseline cache | `0` | No vehicle reached crossing-baseline handling. |
| Batch wire bytes | `0` | No spatial batch was created or published. |

The current `vehicles_processed / input_records_valid` ratio is `0` for Denver. The same ratio is nonzero for the other six cities, including approximately `1.0` for Atlanta, `0.87` for Boston, `0.97` for New York City, `0.99` for Philadelphia, `0.64` for Toronto, and `0.86` for Washington, DC.

### Revision comparison

A 72-hour Prometheus range query shows:

| Revision | Route index | Vehicles processed | Tones emitted | Observation |
|---|---:|---:|---:|---|
| `0000154` | `139` | up to `268` | up to `99` | Denver worked. |
| `0000155` | `0` in the observed startup sample | `0` | `0` | Route-index startup gap. |
| `0000156` | `0` | `0` | `0` | Route index remained unavailable. |
| `0000158` | `139` | `0` | `0` | Index recovered, output did not. |
| `0000161` | `139` | `0` | `0` | Persistent zero-processing state. |
| `0000162` | `139` | `0` | `0` | Current persistent zero-processing state. |

The revision boundary is strong evidence of a deployment-related change or state transition. The later recovery of the route index without recovery of vehicle processing shows that the route-index condition and the current zero-tone condition are not identical.

## Code-path correlation

The worker's processing path explains the metric combination:

- `input_records_valid` is derived from the number of feed entities; it does not prove that every entity has a position or usable route ID.
- Entities with no position are skipped.
- Entities with no `Trip.RouteId` are skipped.
- Entities whose route ID is absent from the route index are counted as unknown-route skips.
- Entities for which spatial snapping returns no result are skipped.
- `vehicles_processed` increments only after a successful route lookup and snap.
- A zero batch means publishing is never attempted, so this is not currently a SignalR publish failure.

Relevant implementation: [Worker.cs](../../src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Worker.cs) and [CityFetchResult.cs](../../src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/Metrics/CityFetchResult.cs).

The Denver configuration contains the expected RTD rail mappings (`101C` → `C`, `101E` → `E`, and the other RTD rail IDs), and `GtfsRtCity` applies that map before reconciliation. A missing map is therefore not supported by the current source configuration. Deployed configuration/image drift remains possible and must be checked independently.

## Root-cause assessment

### Confirmed

- Denver's current worker is alive and cycling.
- Denver's GTFS-RT input fetch is succeeding.
- Recent, nonzero input records are arriving.
- The current route index and trigger-point cache are populated.
- No Denver vehicles are reaching successful reconciliation.
- No tone batch is being generated or published.
- The condition began across a revision boundary after a previously working revision.
- The earlier route-index-unavailable interval was followed by route-index recovery without tone recovery.

### Most likely current causes

1. The active deployment's live RTD route IDs do not match the route-index keys, despite the mappings present in the repository configuration.
2. The current RTD payload contains entities without usable `Vehicle.Position` or `Trip.RouteId`; the current input metric does not distinguish these cases.
3. The current vehicle coordinates do not spatially snap to the loaded Denver route geometry.
4. Revision `0000162` is running a stale or divergent configuration/image compared with the checked-out source.

### Not supported by current evidence

- A current route-index outage: the index is `139`.
- A current RTD fetch outage: fetch success is `1`, records are nonzero, and input lag is low.
- Crossing suppression as the primary cause: no vehicles reach baseline/crossing processing, and suppression counters are zero.
- SignalR publishing as the primary cause: batch wire bytes are zero, indicating no batch was formed.
- A worker-wide failure: other cities continue processing vehicles and producing tones.

## Investigation limitations

- The metric catalog has no `skippedNoJoinKey`, `skippedUnknownRoute`, position-validity, or snap-failure metric.
- The Grafana Loki application-log datasource returned no label names or values for the investigated period, so the detailed per-cycle skip breakdown could not be retrieved from Grafana.
- A direct current RTD protobuf download and decode was not completed from the investigation environment because outbound network access was unavailable.

## Next steps

1. Retrieve Azure Container App worker logs for revisions `0000155` through `0000162` and locate the per-cycle reconciliation breakdown: `skippedNoJoinKey`, `skippedUnknownRoute`, moved, unchanged, stationary, stale, and crossing counts.
2. Confirm the effective configuration inside the active revision, especially the Denver `RailRouteIdMap`, feed URL, city name, and route-shape API URL.
3. Decode a current RTD `VehiclePosition.pb` sample and compare, per entity:
   - presence of `Vehicle.Position`;
   - presence of `Trip.RouteId`;
   - raw route IDs after the rail remap;
   - route IDs present in the loaded static index;
   - latitude/longitude distance from the corresponding route geometry.
4. Compare the route-shape payload served to revisions `0000154` and `0000162`, including route keys and city partitioning, if historical artifacts are available.
5. Check the deployment diff and image/config provenance between the last working revision and `0000155`/`0000162`.
6. Add bounded metrics for route-key misses, missing route IDs, missing positions, snap failures, and publish attempts so future zero-tone incidents can be localized without relying on debug logs.
7. Keep the incident open until Denver records are successfully snapped, the vehicle-state cache becomes nonzero, batches are published, and tones resume across multiple polling cycles.

## Current status

Denver remains in an ongoing zero-tone state. The investigation has narrowed the failure to the pre-crossing spatial reconciliation path after a deployment/revision transition, but the exact rejection branch is not yet confirmed. No production configuration or Grafana resource was changed during this investigation.
