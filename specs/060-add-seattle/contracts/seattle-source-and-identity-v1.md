# Seattle source and identity contract v1

## Source matrix

| Logical source | Endpoint | Access | Reported source key |
| --- | --- | --- | --- |
| KCM vehicles | `https://s3.amazonaws.com/kcm-alerts-realtime-prod/vehiclepositions.pb` | Keyless GET | `seattle-kcm-vehicles` |
| ST agency 40 vehicles | `https://api.pugetsound.onebusaway.org/api/gtfs_realtime/vehicle-positions-for-agency/40.pb` | GET with `key` query parameter read only from `SOUND_TRANSIT_OBA_API_KEY` | `seattle-st-agency40-vehicles` |
| KCM static | `https://www.soundtransit.org/GTFS-KCM/google_transit.zip` | Keyless GET | N/A |
| ST static | `https://www.soundtransit.org/GTFS-rail/40_gtfs.zip` | Keyless GET | N/A |

The ST path matches the existing [key acquisition research](../../../docs/city-compat/KEY_ACQUISITION.md). The implementation must pin the verified endpoint in both host configs and test the effective URI without printing its key. The consolidated regional ZIP is forbidden in every path, including fallback or geometry supplement. Redirect behavior must not forward the key to another host. If ST credential is required and absent, record ST failure; never silently use a keyless protected request as proof of intended access. Static requests never inherit the realtime key.

## Per-response processing

1. Fetch/decode each endpoint independently with existing bounded cancellation and safe logging.
2. Immediately timestamp each decoded response. When a valid reported source catalog is active for Seattle, create the existing `ReportedVehicleSourceObservation` from all nondeleted Vehicle records' exact descriptor IDs. No route, geometry, position or display filter applies. Malformed/failed/empty sources retain distinct outcomes independently of reported capture enablement.
3. Resolve route IDs against the validated direct-static alias map. Known joins include `100451 -> 556`, `102767 -> 547`, `100479 -> 1 Line`, `2LINE -> 2 Line`. Missing or ambiguous geometry excludes processed display coverage. 512 is excluded.
4. On mapped shared ST Express routes `556`/`547`, a same-descriptor pair is corroborated only if both candidates are eligible, vehicle timestamps differ by at most 120 seconds and positions by at most 5 km. Prefer KCM for a corroborated pair. Use eligible ST alone when KCM has no possible counterpart for that descriptor on the same shared route; a KCM record mapped to a different route is independent. A KCM record with the same descriptor on the same shared route but missing position, time, geometry or freshness evidence is still a possible counterpart: withhold that pair rather than treat ST as a fallback. Also withhold a pair that exceeds either bound. Emit a bounded, non-sensitive ambiguity reason and preserve both raw source observations. Do not deduplicate solely by entity ID or equal descriptor text. Source-qualify unrelated display state identities after raw extraction. Validate these thresholds with broader service-hour fixtures before release.
5. Return one `CityFetchResult` with both source counts/observations and the selected processed feed. One failed source does not suppress eligible vehicles from the other. Worker capture runs before route processing; the selected feed drives map, route-hour movement and audio.

## Reported identities

The existing [capture contract](../../059-reported-vehicles/contracts/reported-vehicle-capture-v1.md) and [reporting contract](../../059-reported-vehicles/contracts/reported-vehicle-reporting-v1.md) remain unchanged. Leave Seattle's reported source-policy arrays absent while unsupported; this is the current catalog's explicit unsupported-city mechanism and must coexist with whatever global flags are actually configured. Every endpoint requires an explicit stable policy before Seattle collection is enabled; `SourceKey` is unique, `IdentityKind` is `vehicle-descriptor-id`, `IdentityRuleVersion` is `descriptor-id-v1`, and fallback identity is false. `IdentityNamespace` and cadence/gap require evidence. Four paired shared IDs prohibit assuming source-specific domains yield a distinct physical fleet; they do not prove an endpoint-wide common namespace. Existing Stats retains Seattle in the city/hour denominator and reports unavailable/partial coverage, not zero. Do not substitute a filtered processed feed for raw capture or flip global read/collection settings to hide this limitation.

## Verification cases for implementation

- Request construction proves only the ST destination receives `key`; KCM and both static destinations receive no credential, including redirects.
- Decoded success, successful-empty, timeout, non-2xx, malformed payload and partial-source cases retain correct source coverage and receipt times.
- Direct-static union resolves known route IDs and rejects ambiguous aliases/display keys; 512 has no processed vehicle.
- Paired 556/547 vehicles yield one processed vehicle, one movement/history contribution and one audio contribution; unrelated equal descriptors remain separate; ST fallback works when no possible KCM counterpart exists, while a same-route KCM record lacking comparison evidence causes withholding.
- Original raw descriptor records from both sources, including 512 and map-suppressed duplicates, remain eligible for reporting under a later valid policy; no invented fallback IDs. With absent Seattle policies, an already-enabled global collector starts cleanly and Stats qualifies Seattle/All as unsupported.
