# EMT Madrid — bus compatibility reassessment

**Evaluated:** 2026-10-24. Supersedes the 2026-08-14 report.

**Verdict: CONDITIONAL-GO for access and feed validation; not ready for implementation.** A publisher-owned fleet-position collection has now been found in addition to the documented arrivals coordinates. Its current data API and live health remain unverified: the published/derived v1 paths returned 404. Prefer resolving this fleet source over polling individual stops. The earlier claim that EMT has no vehicle positions in any format was incorrect.

The full Madrid spike is in [madrid.md](madrid.md). Retained evidence includes the [initial static/alerts measurements](evidence/madrid-2026-10-24.json) and the [deeper fleet/access investigation](evidence/madrid-metro-emt-2026-10-24.json), performed approximately 11:51–11:56 UTC on 2026-10-24.

## What was measured

- The [official static ZIP](https://datos.emtmadrid.es/dataset/9b23259a-4491-494b-9695-36a7709b2c12/resource/3cba2058-9833-422c-a704-bf992d31d2ee/download/gtfs_emt.zip) returned HTTP 200 and decoded: 21,206,013 bytes, 237 bus routes, 88,516 trips, 4,933 stops, 474 shapes and 194,812 shape points. All 237 route rows have a first referenced shape. Calendar coverage spans 2026-10-23–2026-12-31; 221 route rows have service on the evaluation date. No key was needed.
- Two separately fetched updates from `https://openapi.emtmadrid.es/v1/bus/servicealerts/proto` decoded to 158 alerts each and **zero vehicle entities**. This endpoint is not a vehicle-position feed; it was not an empty vehicle-service snapshot.
- An unauthenticated POST to `https://openapi.emtmadrid.es/v3/transport/busemtmad/stops/62/arrives/all/` returned HTTP 401. No authenticated bus data was measured.

## What changes the old conclusion

EMT's [official 2025-03-17 announcement](https://www.emtmadrid.es/Noticias/Madrid-cuenta-con-la-posicion-de-los-autobuses-de.aspx) says it supplies GTFS-RT vehicle positions to Google. This does not prove that TransitJazz can access that endpoint.

The [official API documentation](https://apidocs.emtmadrid.es/) and its [served schema](https://apidocs.emtmadrid.es/api_data.js) describe the arrivals response's `Arrive.geometry` as bus position, alongside `bus` and `line`. Examples show distinct coordinates for different buses. These are documented examples, not observed live positions. The current [public catalogue](https://datos.emtmadrid.es/dataset/tiempo-real-para-autobuses-de-emt) lists alerts and authenticated arrivals; a public fleet-wide position feed was not established.

The correct blocker is **unverified authenticated access, freshness, coverage and operational fit**, rather than a proven absence of source data.

## Newly found fleet-position collection

The [official transport collection listing](https://mobilitylabs.emtmadrid.es/es/Portal/collections?category=1) contains a collection titled **Posición de la Flota de Autobuses de EMT**, owned by Mobilitylabs and dated 2018-10-04. Its [detail page](https://mobilitylabs.emtmadrid.es/es/portal/collectionsDetail/ff594c7a-8a7c-423a-8a06-c14a4fac5bff) was fetched successfully during this investigation:

| Published property | Observed value |
| --- | --- |
| Collection ID | `ff594c7a-8a7c-423a-8a06-c14a4fac5bff` |
| Category / subcategory | TRANSPORTE / BUSMADRID |
| Data type / sharing label | REACTIVE / PUBLIC |
| Geographic-information flag | Present/enabled |
| Reactive-file reference | `flotapos` |
| Reference description | Fleet positions for EMT Madrid buses |
| Documentation link | [Publisher GitLab](https://gitlab.com/mobilitylabsmadrid) |

The description promises real-time bus locations, making this a stronger fleet-level lead than per-stop arrivals. However, a catalogue entry dated 2018 is not proof of a working current data feed. The [schema page](https://mobilitylabs.emtmadrid.es/es/portal/collectionScheme/ff594c7a-8a7c-423a-8a06-c14a4fac5bff) redirects to `/es/login`. No live fleet payload or schema was obtained.

The served API documentation describes a collection-discovery operation and a reactive-read POST with an optional empty JSON filter and `accessToken` header. Bounded probes were made without credentials:

| Operation | Path under `https://openapi.emtmadrid.es` | Result |
| --- | --- | --- |
| GET collection metadata, using documented template | `/v1/mobilitylabs/discover/collection/ff594c7a-8a7c-423a-8a06-c14a4fac5bff/` | HTTP 404 |
| POST reactive read, derived from documented template plus published `flotapos` reference; body `{}` | `/v1/mobilitylabs/collection/reactive/ff594c7a-8a7c-423a-8a06-c14a4fac5bff/flotapos/` | HTTP 404 |
| POST current arrivals route; body `{}` | `/v3/transport/busemtmad/stops/62/arrives/all/` | HTTP 401 |

These results distinguish a current protected arrivals route from unresolved fleet routing. They are not successful-empty responses and do not establish that acquiring a key will repair the fleet URL. The reactive reference may require a resolved link ID or a newer API version/host; ask the publisher for the supported route and scope. Do not guess a production endpoint or claim the catalogue's PUBLIC label permits anonymous dynamic access.

The official GitLab group API returned an empty public-project list in this probe, so that documentation link did not supply a working replacement endpoint. This does not establish that no examples or private documentation exist.

## Preferred path versus fallback

| Candidate | Work needed | Decision |
| --- | --- | --- |
| MobilityLabs `flotapos` fleet collection | Confirm current route/version, token scope, complete response shape, vehicle/line IDs, timestamps and quota; measure multiple updates | Preferred next access investigation; no live compatibility verdict yet |
| GTFS-RT positions shared with Google | Ask EMT whether an authorized developer feed is available, with URL, permitted use and request budget | Potential alternative; announcement alone does not grant access |
| Authenticated per-stop arrivals | Custom login/POST/JSON adapter; curated stop selection, coverage measurement and cross-stop vehicle deduplication | Fallback for an explicitly limited bus subset; unsuitable to assume complete fleet coverage |
| Public alerts protobuf | No vehicle entities in either decoded update | Cannot drive vehicle motion |

One working fleet endpoint would require only **4,320 requests/day at 20 seconds** or **2,880/day at 30 seconds**, per active instance before login/retries. Those are calculated possibilities, not measured cadence or granted quota. Two continuously active environments double demand. Polling the 4,933 static stops every 30 seconds requires **14,207,040/day per instance**; the fleet lead materially changes the architecture to investigate first.

## Required next evidence

A human registers an application at [MobilityLabs](https://mobilitylabs.emtmadrid.es/) and confirms current access, terms and quota. First establish whether the fleet collection is still offered and obtain its supported endpoint/version. The documented session flow logs in with `X-ClientId` / `passKey` headers and sends `accessToken` on data calls; this is not evidence of an OAuth grant. `EMT_CLIENT_ID` / `EMT_PASS_KEY` would be proposed local credential names; no values should enter source control or chat. Confirm granted scopes, `tokenSecExpiration`, renewal behavior and actual `apiCounter.dailyUse` without recording credential/token values.

Sample multiple authenticated updates during service. Check bus IDs, coordinates/timestamps, line-to-static joins, duplicates across stops, successful-empty versus failed requests and the fleet subset observed. None of those bus compatibility measurements is available yet.

The served documentation's quota prose says 25,000/day basic and 250,000/day advanced, but embedded legacy examples mention 20,000/150,000. Actual account limits are **unverified**. Polling all 4,933 stops every 30 seconds would require **14,207,040 requests/day per instance**, before retries/login. A key alone does not make that design feasible. Investigate authorized fleet-feed access first; a selected-stop alternative must disclose limited coverage.

The generic adapter cannot perform EMT's session-header authentication, POST requests or JSON decoding. A bespoke component would be needed if authenticated measurements pass. Current worker indexing aliases retained raw route IDs and short names: do not assume leading-zero normalization is necessary before observing live `line` values. Combining buses with Cercanías introduces measured C1/C2 display-key collisions.

## Acceptance measurements still required

| Check | Evidence needed before implementation |
| --- | --- |
| Payload and health | Real authenticated decode across updates; distinguish success-empty, auth failure, not-found, timeout and parse failure |
| Position | Actual per-bus coordinates, WGS84/order confirmation, per-observation timestamp semantics, age distribution and plausible movement; response time is not automatically GPS time |
| Identity | Descriptor coverage and duplicates; prove cross-stop records can be joined to one bus; avoid inventing fallback IDs |
| Route join | Distinct and vehicle-weighted matches to EMT static IDs/short names, unmatched denominators and actual example joins; don't assume zero-padding fixes |
| Coverage | Scope of fleet collection or selected stops, active route spread and observed bus population; catalogue title does not prove complete fleet coverage |
| Operational fit | Actual update cadence/bytes, account quota, all environments/instances/retries, token refresh load and measured worker cost |

If validated, use a focused EMT component that normalizes the measured JSON into the existing vehicle model, retaining raw observation timestamps and source outcomes. The generic GTFS-RT adapter is insufficient for this API. Reported-vehicle capture must deduplicate the same bus across stop requests in a shared EMT identity namespace; transient IDs and partial-stop visibility need truthful count coverage. A combined city must also resolve Metro's numeric route-name collisions with EMT, in addition to Cercanías C1/C2.

## Terms, attribution and operator questions

The [MobilityLabs terms](https://mobilitylabs.emtmadrid.es/sip/terms-of-use) permit commercial/noncommercial reuse under their conditions, require a personal identifier for dynamic web services, and require retained update/license metadata. They call for **Powered by EMT de Madrid**, preferably linked to EMT; the API example separately names EMT Madrid MobilityLabs. The published agreement renews annually automatically, and EMT may impose identifier expiry/renewal with advance notice. A human must confirm the current issuance process and applicable dataset-specific conditions. The static catalogue labels its license `odc-by`; the realtime catalogue is separately `odc-odbl`. Catalogue metadata alone does not settle the fleet collection's exact terms.

The precise questions for `mobilitylabs@emtmadrid.es`, published in the operator's documentation, are:

1. Is collection `ff594c7a-8a7c-423a-8a06-c14a4fac5bff` / `flotapos` still available, and what is its supported host, API version and read operation?
2. Can a registered external application access the fleet collection or the GTFS-RT positions feed shared with Google? What token scopes and account tier are needed?
3. What are the current quota, allowed polling cadence, response size and fleet/route coverage? Are quotas shared across token refreshes, endpoints and environments?
4. Which fields identify a bus, its line, WGS84 position and source observation time? How long are identities stable?
5. What attribution, redistribution/derived-music conditions, renewal and support expectations apply?

These questions were recorded, not sent. No account was registered, credential obtained, adapter implemented or deployment performed. Metro and Cercanías are separate sources; see the city report.
