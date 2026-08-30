# GTFS Compatibility Report — TfL (Transport for London) (London, UK)

> ## 0/100 — Not Viable
> Bus: 0/70 (blocked — no live feed measured) · Rail: 0/20 · Credential: 0/10 (blocked)
> Ceiling applied: NO-USABLE-FEED, capped at 15
> No usable GTFS-RT feed format exists in any form — buses, Underground, DLR, Overground,
> and the Elizabeth line are all served exclusively by TfL's proprietary Unified API. Even
> a registered key would not make this consumable without a net-new bespoke adapter.

**Evaluated:** 2026-08-05

## Feed health

| | |
|---|---|
| **Blocking classification** | **NO-USABLE-FEED** |
| Static GTFS URL | None published — not verified. TfL's own 2020 Freedom of Information response states it does **not currently use or produce GTFS data** (`FOI-0121-2021`, tfl.gov.uk/corporate/transparency/freedom-of-information). A separate, narrower "station topology" dataset discussed on TfL's developer forum is stops/pathways-only and was reported by developers to violate the GTFS spec — it is not a routes/trips/shapes feed and does not substitute for a static GTFS zip. |
| GTFS-RT vehicle positions (buses) | **Only a proprietary non-GTFS-RT API (JSON) is published — no protobuf equivalent exists.** Bus arrivals are served by the "Countdown" Live Bus & River Bus Arrivals API, a custom URA-derived JSON/XML interface (`countdown.api.tfl.gov.uk/interfaces/ura/instant_V1`) that returns per-stop arrival predictions (vehicle ID + stop lat/lon), not a GTFS-RT `VehiclePosition` entity stream. A direct unauthenticated request made in this run returned `HTTP 400 Bad Request` with no body; TfL's own developer forum confirms the endpoint requires registered credentials for real use. |
| Rail realtime (trains) | **Only a proprietary non-GTFS-RT API (JSON) is published — no protobuf equivalent exists.** Underground/DLR/Overground/Elizabeth line real-time predictions ride the same TfL Unified API (`api.tfl.gov.uk`) via `StopPoint`/`Line` arrivals endpoints — signalling-derived arrival predictions, not raw vehicle GPS (most of the network runs in tunnels with no GPS fix available). A direct unauthenticated request to `Line/Mode/tube/Status` in this run returned `HTTP 200` with a proprietary JSON schema (`Tfl.Api.Presentation.Entities.*`) containing service-status/disruption fields only — zero latitude/longitude or vehicle-position fields of any kind. |

Searched in order per the discovery playbook: TfL's own developer portal (`api-portal.tfl.gov.uk`, `tfl.gov.uk/info-for/open-data-users/`), the Mobility Database (lists a third-party-derived static GTFS-style entry for the Underground, not an agency-published feed), and targeted `WebSearch`. This is a structurally different problem from a route-ID or field-completeness gap: TfL has never published a GTFS-RT protobuf feed of any kind for any mode, keyed or keyless, so there is no protobuf for the worker's decoder to read regardless of credentials.

## Static GTFS (verified by direct parse)

Not assessed — static GTFS zip was not reachable (TfL does not publish one; see Feed health above).

## Vehicle positions / route ID alignment (buses)

**Not assessable without a live feed.** No GTFS-RT `.pb` exists for TfL buses at any URL — the only real-time surface is the proprietary, credential-gated Countdown API, which returns per-stop-prediction JSON rather than a decodable `FeedMessage`. Field completeness (route_id %, lat/lon %, speed/bearing %) cannot be measured until a bespoke adapter is built to normalize Countdown's response shape, independent of any key.

## Verdict

- **Buses: INCOMPATIBLE (NO-USABLE-FEED)** — no protobuf feed exists; only a proprietary, credential-gated JSON arrivals API with a different data shape (per-stop predictions, not per-vehicle positions).
- **Rail: INCOMPATIBLE (NO-USABLE-FEED)** — same story; Underground/DLR/Overground/Elizabeth line predictions ride the same proprietary Unified API, and most of the network has no GPS fix to report even if a protobuf wrapper existed.

| Check | Result |
|---|---|
| Required fields (route_id + lat/lon) | **UNASSESSED** — no protobuf feed reachable for any mode |
| Route ID alignment (buses) | **N/A** — nothing to align against |
| Rail line alignment | **N/A** — no static GTFS exists to cross-reference published line codes against |
| Blocking classification | **NO-USABLE-FEED** |

**Bottom line:** TfL is the outlier among agencies evaluated so far in that *nothing* is clean here — not even the static side. TfL has publicly confirmed (via FOI) that it does not produce GTFS at all, and its real-time data for every single mode (bus, Underground, DLR, Overground, Elizabeth line) is served exclusively through one proprietary Unified API family (Countdown for buses, StopPoint/Line arrivals for rail) that returns stop-level arrival predictions rather than GTFS-RT vehicle positions. Onboarding TfL would require building both a static-schedule ingestion path (there is no zip to point `GtfsStaticLoader.cs` at) and a net-new realtime adapter normalizing a fundamentally different data shape (predictions, not positions) into the platform's feed format — substantially more effort than any other agency evaluated to date, and not resolvable by obtaining a key alone.

## Adding TfL as a data source

- **Static GTFS zip:** none exists — TfL does not publish one. A static schedule would need to be sourced from TfL's TransXchange-format data (a different, UK-specific schedule format) and converted, or reconstructed from the Unified API's `Line`/`Route`/`StopPoint` endpoints — non-trivial new ingestion work independent of the realtime problem.
- **Bus realtime:** Countdown Live Bus & River Bus Arrivals API (`countdown.api.tfl.gov.uk`) — **registered credentials required**, and even once obtained, **no GTFS-RT equivalent exists**; would need a net-new `ITransitCity` implementation normalizing Countdown's per-stop-prediction JSON into the platform's feed format, mirroring the one bespoke city implementation that already does this for a different agency.
- **Rail realtime:** TfL Unified API `StopPoint`/`Line` arrivals endpoints (`api.tfl.gov.uk`) — same NO-USABLE-FEED treatment as bus realtime; these are signalling-derived arrival predictions, not vehicle positions, so even a config-only `RailRouteIdMap` remap would not apply — a bespoke adapter is the only path.
- **Auth:** an `app_id`/`app_key` pair registered through TfL's API Portal (`api-portal.tfl.gov.uk`) for the Unified API generally, plus separate Countdown API access. Any key obtained must be stored via env/secrets, never committed, per the existing MARTA rail key precedent.
- **Config entry vs. new code:** NO-USABLE-FEED on both axes — requires two new bespoke adapters (static ingestion + realtime prediction normalization); there is no existing config-only path for this feed shape.
- **Effort scope:** both a key AND substantial new adapter code, on both the static and realtime sides — the largest effort scope of any agency evaluated so far, exceeding even CTA's two-proprietary-API case because CTA at least publishes a standard static GTFS zip.

## Open items for a follow-up pass

- Obtain TfL Unified API + Countdown API registered credentials.
- Investigate whether TfL's TransXchange-format schedule data (or the Unified API's `Line`/`Route`/`StopPoint` endpoints) can be converted into a usable static route/shape set, since no ready-made static GTFS zip exists.
- Pull a live Countdown sample once access exists; determine whether per-stop vehicle IDs can be reassembled into per-vehicle positions (the platform's required shape), since the raw feed is prediction-oriented rather than position-oriented.
- Pull a live Unified API rail-arrivals sample to confirm whether any GPS-based positions exist for above-ground segments (Overground, Elizabeth line, DLR) even though deep-tube sections cannot have any.
