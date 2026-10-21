# Seattle data model and invariants

This feature adds configuration and transient processing state to existing models. No Seattle database table or migration is planned.

## Seattle city registration

| Field | Value / rule |
| --- | --- |
| `Name` / public slug | `seattle`, exactly one city entry |
| Display name / Stats key | Seattle / `StatsCitySeattle` |
| `TimeZoneId` | `America/Los_Angeles`; existing Stats city-local date policy |
| Realtime sources | KCM S3 keyless and ST OneBusAway agency 40 with `key` from `SOUND_TRANSIT_OBA_API_KEY` |
| Static sources | KCM OTD ZIP and ST OTD direct ZIP, both keyless |
| Origin | Transit-dense Seattle center selected/verified in the client map-origin configuration |
| History selectors | Explicitly checked against existing disabled/global gates; no automatic activation |

Both host JSON entries must have the same identity, source order, endpoints, time zone and source policy. Existing city array indices remain stable. The client has one picker/hash identity.

## Agency source and raw observation

Each source has an exact endpoint, access method and source-specific fetch outcome. Candidate reported source keys are `seattle-kcm-vehicles` and `seattle-st-agency40-vehicles`; future valid policies use `IdentityKind=vehicle-descriptor-id`, `IdentityRuleVersion=descriptor-id-v1` and no fallback. Identity namespaces are **not finalized**: source-specific domains overcount verified overlapping vehicles and a global shared domain is not justified by four observed pairs. Both Seattle reported policy arrays remain absent in runtime configuration, so an already-enabled global collector treats Seattle as unsupported. When a valid policy is activated, each source's receipt UTC and raw nondeleted Vehicle descriptor IDs are retained before map filters.

A successfully decoded empty feed is successful-empty with zero raw identities. Failure or malformed feed is failure, not empty. Capture records the source response at receipt before route, coordinate, freshness, geometry, overlap or 512 filtering; `CityFetchResult` retains both source observations even if one source fails. No raw descriptor value enters public output or logs.

## Mapped route

For each direct static ZIP, `route_id`, `route_short_name` and geometry determine `RouteJoinKey` using the existing `RouteShapeProperties.JoinKey` rule. The union is keyed by display `RouteJoinKey`, and Worker aliases each static `route_id` to that route. Current measured examples: `100451 -> 556`, `102767 -> 547`, `100479 -> 1 Line`, `2LINE -> 2 Line`. An ambiguous display key or raw ID mapping is invalid for Seattle and must not silently select a first route. Route 512 has no direct-agency geometry and no map/audio representation at launch; its raw source observation remains eligible under the reported capture contract once that feature is supported.

## Display vehicle and overlap group

A display candidate is a source-tagged Vehicle entity with original descriptor, mapped route, position/timestamp quality and eligibility. An overlap group requires matching descriptor IDs on the verified shared ST Express routes `556`/`547`, compatible canonical route, timestamps within 120 seconds and positions within 5 km. These bounded thresholds are a design starting point requiring service-hour validation. Choose KCM for a corroborated eligible pair. Use eligible ST alone when KCM has no possible counterpart on that descriptor and shared route; a KCM record mapped to a different route is independent. If KCM has the same descriptor on the same shared route but lacks eligibility or comparison evidence, withhold the possible pair with a bounded reason, as with contradictory space/time evidence. Equal text outside the shared-route contract remains independent and uses source-qualified state identity. Source qualification applies only to the processed copy after raw observation extraction. Worker receives at most one member/group, so map, route-hour history and audio use one vehicle.

## Coverage and state

`CityFetchResult` aggregates independent successful/failed source counts and, once a valid reported policy is active, original source observations. Its processed feed may contain fewer entities than either raw response because of route/geometry/overlap selection. Source success and reported coverage never derive from processed entity count. Existing Stats distance and reported states, completed-hour grids and All-city aggregation remain authoritative. With no Seattle reported source policy, the existing Stats read must show Seattle unavailable and qualify All coverage; newly added route-history hours remain pending/partial until genuine evidence accumulates. No backfill or fabricated zero.

## Release evidence

`validation.md` records check, environment, UTC time, result, evidence link and unresolved issue. It may cite the 2026-10-18 compatibility measurements as preexisting research. Local tests, credential validation, quota, calendar, service-hour, browser, listening and production results are pending until measured. Secret values, full authenticated URLs and raw vehicle identities are excluded.
