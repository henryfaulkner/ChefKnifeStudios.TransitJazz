# Feed Discovery Playbook (STAGE 3 detail)

STAGE 3 is the whole ballgame for a hands-free job — stages 4–6 are near-free (delegated
or mechanical), but stage 3 requires genuinely finding real URLs on the open web and
correctly classifying a dead end rather than fabricating a result. This file is the
load-bearing content the skill invests the most authoring effort into.

## 1. Search order

Search in this order; stop as soon as a canonical URL is found — don't keep searching
past a confirmed hit:

1. **The agency's own developer/open-data portal.** Look for a "developer resources,"
   "open data," or "GTFS" page on the agency's own domain. This is always the canonical
   source when it exists.
2. **The Mobility Database** (`mobilitydatabase.org`, successor to TransitFeeds). Search
   by agency/city name; it lists both static GTFS feeds and, where known, GTFS-RT
   endpoints.
3. **A targeted `WebSearch`** for `"{agency} GTFS realtime vehicle positions"` (and
   variants: `"{agency} GTFS-RT feed"`, `"{agency} developer API transit"`). Prefer the
   agency's own canonical URL over a third-party mirror or a stale blog post.

## 2. The three feed types and how to tell them apart

A GTFS-RT endpoint may be `vehicle-positions`, `trip-updates`, or `service-alerts`.
**Only vehicle-positions drives the worker** — it is the sole feed type the platform's
`ITransitCity`/`GtfsRtCity` path consumes; trip-updates and alerts carry no lat/lon.

- Endpoint paths often name the type (`/vehiclepositions`, `/vehicles` vs. `/tripupdates`,
  `/alerts`, `/servicealerts`). Use this as a first hint, not proof.
- **The authoritative signal is always the decode, not the URL.** After fetching, run
  `mj-gtfs`'s decode and check `vehicle_entities` / `lat_lon_pct`. A feed that decodes to
  zero vehicle entities (or vehicle entities with no position field at all) is not a
  vehicle-positions feed regardless of what its URL suggests.
- If an agency publishes multiple sibling GTFS-RT endpoints, note which ones exist but
  are NOT vehicle-positions in the eventual report's "Adding as a data source" section
  (mirrors `ttc.md`'s note about `/trips` and `/alerts` being unused siblings).

## 3. Failure-mode → verdict table (the classifier)

Every dead end must be classified into exactly one row below before writing anything.
This classification is REQUIRED, not optional narrative — it determines which BLOCKED
sub-reason (KEY-GATED vs. NO-USABLE-FEED) the report uses, which in turn sets the
aggregate score's hard ceiling.

| Symptom | Verdict | Sub-reason | What to write |
|---|---|---|---|
| No GTFS-RT feed published at all (searched portal, Mobility Database, WebSearch — nothing found) | BLOCKED | **NO-USABLE-FEED** | Static health + "no realtime feed of any format is published" (mirrors `cta.md`'s static-only case). |
| GTFS-RT exists but only trip-updates/alerts endpoints — no vehicle-positions endpoint found | BLOCKED | **NO-USABLE-FEED** | State plainly that a GTFS-RT gateway exists but carries no vehicle positions. |
| Feed exists in standard GTFS-RT protobuf format but 200s only with a registered API key not already available in the environment | BLOCKED | **KEY-GATED** | State the feed format is otherwise standard/consumable; key acquisition is out of scope for this run; note the platform already supports a config-only key once obtained (`ApiKeyEnvVar`/`ApiKeyQueryParam`). Do NOT attempt to register for a key. |
| Only a proprietary non-GTFS-RT API is published (XML/JSON, agency-specific schema), with or without a key | BLOCKED | **NO-USABLE-FEED** | State that no protobuf equivalent exists; a bespoke adapter would be needed regardless of any key (mirrors `cta.md`'s Bus Tracker/Train Tracker framing). If it's ALSO key-gated, still classify NO-USABLE-FEED — the missing protobuf format dominates, since even a key wouldn't make it consumable without new code. |
| Feed reachable, decodes, but 0 vehicle entities at this specific time of day | Provisional — retry once if cheap; otherwise BLOCKED | **NO-USABLE-FEED** (unless retry succeeds) | If a retry is cheap and available within this run, retry once and note the time-of-day caveat if it stays at 0. If not retried or still 0, classify NO-USABLE-FEED with the time-of-day caveat stated explicitly — do not silently guess this was a fluke. |
| Feed reachable, decodes, but 0% `route_id` | NOT blocked — proceed to STAGE 4 | n/a | This is an evaluation finding (route-ID mismatch), not a discovery failure — write the full COMPATIBLE/PARTIAL report per the interpretation table; likely fixable transform. |
| Static zip 404 / moved (CKAN-style resource IDs rotate — see `ttc.md`'s note) | Retry the portal's current link once | n/a if recovered | If unrecoverable after one retry via the portal's current listing, note the stale-URL reason in the report; this affects only the Static GTFS section, not necessarily the bus/rail blocking classification (a static-zip failure alongside a working RT feed is unusual but treat each independently). |

## 4. The "do not fabricate" rule (bluntly)

Every number in the report must come from an actual `mj-gtfs` decode of a real download.
If a feed couldn't be fetched or decoded, the corresponding fields are `UNASSESSED` or
`N/A` — never a guessed percentage. `cta.md` is the model: it says "Not assessable
without a live feed" rather than inventing coverage stats. If you find yourself about to
type a number you didn't get from a tool-call output, stop — you're either fabricating or
you picked the wrong template.

## 5. Auth boundary

The job may pass an API key it *already has available in the environment* (rare — check
before assuming this applies). It must **never**:

- Create developer accounts or agree to terms of service on an agency's behalf.
- Solve registration flows, CAPTCHAs, or email-verification steps.
- Fabricate, guess, or reuse a key from a different agency.

Key-gated with no key already in hand = BLOCKED (KEY-GATED sub-reason), full stop. This
is not a failure of the run — it is the correct, honest outcome to report.

## 6. Rail feed discovery (only pursue if static shows `route_type=1` routes)

- Only search for a rail-realtime API if stage 4's static parse shows `rail_route_count >
  0` for this authority. Absence of a rail feed for a rail-running agency is `N/A`, not
  BLOCKED — the agency can still be bus-compatible on its own axis.
- Rail realtime rarely rides the same GTFS-RT protobuf feed as buses; look for a separate,
  agency-specific API (JSON/XML), mirroring MARTA's `traindata` endpoint or CTA's Train
  Tracker. If published developer docs list valid line/route codes, that alone (without a
  live fetch) can support the BLOCKED template's optional rail line-key desk-check section.
