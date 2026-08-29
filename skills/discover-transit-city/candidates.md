# Candidate Pool — Discover Transit City

A ranked seed list `discover-transit-city`'s STAGE 1 walks top-to-bottom. **Ranked by
likelihood of a clean, keyless, standard-GTFS-RT-protobuf result** — early runs should
produce good PRs and build confidence, so keyless North American agencies are ranked
above European agencies with known registration gates.

Dedup is computed live each run against `docs/city-compat/*.md` (by City + Authority
parsed from each report's H1), never by striking rows here — this file stays a static
seed list and never needs an edit after a run.

**Excluded at authoring time (already evaluated):** `{marta, mbta, wmata, nymta, ttc,
cta}` — MARTA is the app's home agency; the other five already have reports in
`docs/city-compat/`.

**Builder note:** feed URLs rot. Cells below marked with a known URL are a shortcut for
STAGE 3, not a guarantee — STAGE 3 must still verify the feed is live and is specifically
a vehicle-positions endpoint before trusting a pre-filled cell. Several EU agencies
(TfL, IDFM) are known to gate GTFS-RT behind free registration and will likely land as
BLOCKED (KEY-GATED) on first pass — that is expected and fine per D2.

| Rank | City | Authority (official name) | Region | Known static zip URL | Known GTFS-RT vehicle-positions URL | Rail? | Notes |
|---|---|---|---|---|---|---|---|
| 1 | Philadelphia, PA | SEPTA (Southeastern Pennsylvania Transportation Authority) | NA | | | Yes | Keyless GTFS-RT historically; also runs heavy rail (Broad Street/Market-Frankford) + regional rail |
| 2 | Portland, OR | TriMet | NA | | | No | Keyless GTFS-RT; TriMet is a well-known clean, standard GTFS/GTFS-RT publisher; MAX light rail is route_type=0/tram, not heavy rail |
| 3 | San Francisco, CA | SFMTA (San Francisco Municipal Transportation Agency) / Muni | NA | | | Unknown | Muni Metro is light rail (route_type=0), not heavy rail — verify route_type breakdown in static at stage 4 |
| 4 | Denver, CO | RTD (Regional Transportation District) | NA | | | Yes | Runs heavy rail (commuter rail lines); historically keyless GTFS-RT |
| 5 | Seattle, WA | King County Metro | NA | | | Unknown | Link light rail is a separate Sound Transit operation — verify authority boundary at stage 2 |
| 6 | Minneapolis–St. Paul, MN | Metro Transit | NA | | | Unknown | METRO light rail lines are route_type=0/tram |
| 7 | San Diego, CA | MTS (San Diego Metropolitan Transit System) | NA | | | Unknown | Trolley is light rail, not heavy rail |
| 8 | Vancouver, BC | TransLink | NA | | | Yes | SkyTrain is automated heavy rail (route_type=1 candidate) — verify at stage 4 |
| 9 | Montréal, QC | STM (Société de transport de Montréal) | NA | | | Yes | Montréal Métro is heavy rail; historically has had GTFS-RT registration requirements — verify current state |
| 10 | Ottawa, ON | OC Transpo | NA | | | No | O-Train is light rail, not heavy rail |
| 11 | London, UK | TfL (Transport for London) | EU | | | Yes | London Underground is heavy rail; TfL's Unified API historically requires a registered (free) API key — likely KEY-GATED |
| 12 | Paris, FR | RATP / Île-de-France Mobilités (IDFM) | EU | | | Yes | Paris Métro is heavy rail; IDFM's realtime API historically requires registration — likely KEY-GATED |
| 13 | Berlin-Brandenburg, DE | VBB (Verkehrsverbund Berlin-Brandenburg) | EU | | | Yes | U-Bahn is heavy rail; verify GTFS-RT availability and auth requirements at stage 3 |
| 14 | Munich, DE | MVV (Münchner Verkehrs- und Tarifverbund) | EU | | | Yes | U-Bahn is heavy rail |
| 15 | Milan, IT | ATM (Azienda Trasporti Milanesi) | EU | | | Yes | Metropolitana is heavy rail |
| 16 | Madrid, ES | EMT (Empresa Municipal de Transportes de Madrid) | EU | | | Unknown | EMT is the bus operator; Madrid Metro is a separate authority (Metro de Madrid) — verify authority scope at stage 2, EMT itself is bus-only |
| 17 | Brussels, BE | STIB/MIVB (Société des Transports Intercommunaux de Bruxelles) | EU | | | Yes | Brussels Metro is heavy rail |
| 18 | Rotterdam, NL | RET (Rotterdamse Elektrische Tram) | EU | | | Yes | Rotterdam Metro is heavy rail |
| 19 | Helsinki, FI | HSL (Helsingin seudun liikenne) | EU | | | Yes | Helsinki Metro is heavy rail; HSL is known for good open-data practices — potentially clean keyless result |
| 20 | Oslo, NO | Ruter | EU | | | Yes | Oslo Metro (T-bane) is heavy rail |
| 21 | Dublin, IE | Transport for Ireland (TFI) | EU | | | No | No heavy rail metro (DART is commuter rail, not typically route_type=1 in TFI's GTFS); verify at stage 4 |
