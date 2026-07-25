# NYC Bus Support — Design Doc

**Status:** Design sketch (no code written)
**Author:** —
**Date:** 2026-07-12
**Prereq reading:** [`docs/city-compat/nymta.md`](city-compat/nymta.md) (feed evaluation, §"Bus" sections),
[`docs/nymta-subway-interpolation-design.md`](nymta-subway-interpolation-design.md) (the sibling
subway feature, shipped as `specs/040-nymta-subway-interpolation/`), `GtfsRtCity.cs`, `MartaCity.cs`,
`CityConfig.cs`, `GtfsStaticLoader.cs`.

---

## 1. Problem statement

NYC subway trains render on the map today (feature 040), via a bespoke `NymtaCity` adapter that
**synthesizes** a position because subway GTFS-RT never publishes one. NYC **bus** is the opposite
situation: the `obanyc` bus GTFS-RT feed already publishes real `lat`/`lon` for every vehicle,
100% of the time, in the same protobuf shape MARTA/WMATA/MBTA already use. Per the feed
evaluation (`docs/city-compat/nymta.md`), bus is `COMPATIBLE — needs a route-ID normalizer, no new
adapter`. There is no missing-coordinate problem to solve; the only real work is making route IDs
line up between the RT feed and the static route registry, then wiring config.

This is a **much smaller** feature than 040: no interpolation, no offset table, no new
`ITransitCity` implementation. It reuses the existing config-driven `GtfsRtCity` — the same class
already running WMATA (bus+rail) and MBTA — plus one genuinely new, reusable piece: a **route-ID
normalization pipeline**, because the existing `CityConfig.RailRouteIdMap` (a static
`Dictionary<string,string>`, used by WMATA to relabel `"RED"` → `"R"`) can't express the
regex-like transforms NYC bus needs (case-fold, suffix rewrite, zero-pad strip).

## 2. Why this is a separate city from subway, not a merge

`MartaCity` and `WmataCity`'s config precedent both merge **bus + rail into one `ITransitCity`
registration** (`MartaCity.FetchVehiclesAsync` merges its bus protobuf feed with rail JSON
entities into one `FeedMessage`; WMATA's single `GtfsRtCity` entry lists both a bus and a rail RT
URL in one `GtfsRtUrls` array). It's tempting to want the same shape for NYC: one "nymta" city,
bus + subway together.

That doesn't fit here, and forcing it would cost more than it's worth:

- **Subway is bespoke, bus is generic.** `NymtaCity` exists *specifically* because subway needs
  synthesis (`ShapeInterpolator`, `StopOffsetTable`, the 24h-cached offset fetch). Bus needs none
  of that — it's a vanilla `GtfsRtCity` config entry, identical in kind to WMATA's or MBTA's.
  Bolting bus fetch+normalize logic into `NymtaCity` would mean either (a) `NymtaCity` growing a
  second, unrelated responsibility (decode-and-normalize instead of synthesize), or (b)
  `NymtaCity` internally constructing and delegating to a private `GtfsRtCity`-like helper —
  either way, the bespoke-adapter file stops being about the one hard problem it was built to
  solve.
- **Static data doesn't want to merge either.** Subway's static zip (`stops.txt`, `stop_times.txt`,
  subway `shapes.txt`) and bus's static zips (NYCT borough zips + MTA Bus Company zip) are
  different files with different schemas serving different consumers (server-side offset builder
  vs. worker route index). `GtfsStaticLoader.BuildCityShapeSetAsync` already treats one `city.Name`
  as one merged shape set from all its `StaticZipUrls` — merging subway's static needs alongside
  bus's would mean teaching that method to special-case *which* zips feed the offset builder vs.
  the route-shape builder, inside a single city's processing. Keeping them as two `Cities:` entries
  keeps that method exactly as it is today.
- **Telemetry gate already assumes one `EmitsTelemetry` per city.** Subway is `false` (synthesized
  data, keep telemetry MARTA-only per the multi-city decision); bus is real GPS and should very
  likely be `true` like every other agency's live vehicles (§7). One shared "nymta" city can't hold
  both answers.

**Decision: two separate `Cities:` config entries**, `nymta` (existing, subway, unchanged) and
`nymta-bus` (new, bus, config-only `GtfsRtCity`). This mirrors the existing precedent that
`GtfsRtCity` is the reusable path and `MartaCity`/`NymtaCity` are the bespoke exceptions — it just
means NYC needs *two* registrations instead of the one MARTA/WMATA/MBTA each need, because NYC is
the first agency evaluated where bus and rail have genuinely disjoint technical requirements
(WMATA's bus+rail merge works because *both* legs are ordinary GTFS-RT with real positions; NYC's
subway leg is not).

### 2.1 Frontend implication

Feature 040 shipped an ad-hoc "New York, NY" entry in the city picker (`CityFab.razor`) pointing at
the `nymta` hash — that already means "NYC subway." Two backend cities don't have to mean two
picker entries: the map is driven by SignalR groups keyed by city name (`Worker.cs`'s
`transitHubPublisher.PublishBatchAsync(city.Name, ...)`), and the frontend's `TransitMap` component
already subscribes to whichever city name is in the URL hash. Two realistic options:

- **(A) Two picker entries** — "New York Subway" (`#nymta`) and "New York Buses" (`#nymta-bus`),
  each a wholly separate map view/session, exactly like Atlanta/DC/Boston today (one hash = one
  city = one `SignalR` group = one map). Zero new frontend plumbing beyond a second `CityFab`
  button; the client already treats every hash as an independent city.
- **(B) One picker entry, unified map** — "New York, NY" shows both subway and bus on the same
  map/audio session. This needs the **client**, not the worker, to join *two* SignalR groups
  (`nymta` and `nymta-bus`) simultaneously and merge both batches into one map render — a new
  capability; today `TransitMap` assumes exactly one city group per session
  (`HubMethods.JoinCity` is called once, with one city name, per `SignalRNotificationService`).

**Recommendation: (A).** It's zero-risk (no new client-side multi-group-join capability), matches
the existing one-hash-one-city architecture exactly, and is trivially reversible/extensible (a
future "unify NYC" feature could still build (B) later without this feature needing to guess right
about it now). (A) is assumed for the rest of this doc; flip to (B) only if you want one unified
NYC experience badly enough to justify the multi-group SignalR client work as part of this
feature's scope.

## 3. What's already reusable, unchanged

- **`GtfsRtCity`** (`TransitDataWorker/Cities/GtfsRtCity.cs`) — fetches each `GtfsRtUrls` entry,
  stream-decodes protobuf (`ProtoBuf.Serializer.Deserialize<FeedMessage>`), merges, and already
  calls `ApplyRailRouteIdMap` (a static dict relabel) before returning. This is the class that will
  run NYC bus; it needs one new hook (§5), not a rewrite.
- **`GtfsStaticLoader.BuildCityShapeSetAsync`** — already downloads/merges **multiple**
  `StaticZipUrls` per city into one shape set (`allRouteToShape`/`allShapes`/`allMeta`, each
  `TryAdd`-ed across zips, precedent: MARTA/WMATA already list 1–2 zips each). Adding a *third* and
  *fourth* URL (one NYCT borough zip + the MTA Bus Company zip) to `nymta-bus`'s `StaticZipUrls` is
  pure config — no code change (§4).
- **`CityConfig.ApiKeyEnvVar`** — already supported (WMATA precedent: `WMATA_API_KEY`). NYC bus RT
  needs a registered key (`gtfsrt.prod.obanyc.com/vehiclePositions?key=<KEY>`); reuse this field
  with e.g. `NYMTA_BUS_API_KEY`.
- **`CityConfig.EmitsTelemetry`** — reuse as-is; bus should very likely be `true` (§7).

## 4. Static data — two zip sources, one route registry

Per the feed evaluation:

| Source | What it covers | Gotcha |
|---|---|---|
| One NYCT borough zip (any of the 5, e.g. `google_transit_manhattan.zip`) | `routes.txt` — citywide, byte-identical across all 5 boroughs | Don't fetch all 5 for `routes.txt`; it's the same 307 rows every time |
| MTA Bus Company zip (`busco/google_transit.zip`) | Separate operator's routes NYCT's zips don't cover at all (Q06–Q115 numbered locals, QM/BXM express) | Genuinely additive — must be fetched, not optional |

**Open question the feed evaluation surfaces but doesn't fully resolve for this design:** the
evaluation's four-fix sequence reaches 98.5% with *one* NYCT zip + the Bus Co zip, then 100% only
after the zero-pad-strip fix — meaning **one representative borough zip's `trips.txt`/`shapes.txt`
is sufficient for route registry purposes**, since `routes.txt` (which is what the
route-ID-matching cares about) is shared. If you also want every borough's *bus stop shapes*
correctly drawn (not just the route registry matched), you'd want all 5 borough zips' distinct
`trips.txt`/`shapes.txt` merged too — `GtfsStaticLoader`'s existing multi-zip merge (`TryAdd`
across zips) already does this for free if all 5 + Bus Co are listed in `StaticZipUrls`. **Decision
for v1: list all 5 NYCT borough zips + the Bus Co zip** (6 URLs total) — it costs nothing extra in
code (the merge logic doesn't care how many zips), and avoids a partial-shape-coverage gap that
would otherwise need re-litigating later. This is the one place this doc goes slightly further than
the feed evaluation's minimum-viable 2-zip finding, to avoid missing shapes for boroughs `trips.txt`
didn't come from.

No `SubwayStopOffsetBuilder`-style parsing is needed — bus uses the *existing* `trips.txt`/
`shapes.txt`/`routes.txt` parse path (`ParseRouteToShapeMap`, `ParseShapes`, `ParseRouteMetadata`)
identically to every other bus-only city. `stops.txt`/`stop_times.txt` (subway-only) are untouched.

## 5. Route-ID normalization — the one new piece

### 5.1 Why `RailRouteIdMap` can't do this

`CityConfig.RailRouteIdMap` is a static `Dictionary<string,string>` — WMATA uses it for a small,
fixed enumeration (`"RED"` → `"R"`, `"RED0"` → `"R"`, etc., 12 entries). NYC bus's four fixes are
**transforms**, not a lookup table:

| Fix | Transform | Not expressible as a static dict because |
|---|---|---|
| Case-fold | `"BX3"` → `"Bx3"`-shaped comparison (case-insensitive match) | Applies to all ~266 route IDs, not an enumerable set of pairs |
| `+` → `-SBS` | `"M15+"` → `"M15-SBS"` | A suffix rewrite rule, not a fixed set of inputs |
| Zero-pad strip | `"Q06"` → `"Q6"` | A regex-shaped transform (`^([A-Z]+)0*(\d.*)$` → group1+group2), applies wherever the pattern matches |
| Second static source | n/a (not a route-ID transform — a data-source concern, §4) | — |

### 5.2 Design: `RouteIdNormalizer`

A small, pure, testable static class — new file
`TransitDataWorker/Cities/RouteIdNormalizer.cs` — that applies an **ordered list of named steps**
to a route ID string:

```csharp
public static class RouteIdNormalizer
{
    public static string Apply(string routeId, IReadOnlyList<string> steps)
    {
        foreach (var step in steps)
            routeId = ApplyStep(routeId, step);
        return routeId;
    }

    static string ApplyStep(string routeId, string step) => step switch
    {
        "uppercase"          => routeId.ToUpperInvariant(),
        "plusToSbs"          => routeId.EndsWith('+') ? routeId[..^1] + "-SBS" : routeId,
        "stripLeadingZeros"  => StripLeadingZeros(routeId),
        _                    => routeId,   // unknown step name: no-op, never throws (config typo shouldn't crash a tick)
    };

    // "Q06" -> "Q6", "BX07" -> "BX7". No letter prefix or no digits after it: unchanged.
    static string StripLeadingZeros(string routeId)
    {
        var match = Regex.Match(routeId, @"^([A-Z]+)0*(\d.*)$");
        return match.Success ? match.Groups[1].Value + match.Groups[2].Value : routeId;
    }
}
```

**Ordering matters** and is exactly the sequence the feed evaluation measured
(uppercase → plusToSbs → stripLeadingZeros), because `stripLeadingZeros`'s regex assumes an
uppercase letter prefix, and `plusToSbs` must run before the zero-strip would otherwise see a
trailing `+` as part of "the rest of the string" (harmless either order in practice, since `+` isn't
a digit, but keeping the measured order is the safest default — it's the one the 100%-match number
was actually achieved with).

### 5.3 Config surface

New field on `CityConfig`:

```csharp
public string[] RouteIdNormalization { get; set; } = [];
```

For `nymta-bus`: `["uppercase", "plusToSbs", "stripLeadingZeros"]`. Every other existing city
(`marta`, `wmata`, `mbta`, `nymta`) leaves this empty/absent — zero behavior change for them
(`Apply` with an empty step list is a no-op passthrough).

### 5.4 Where it's invoked

Inside `GtfsRtCity.FetchVehiclesAsync`, applied to each entity's `Trip.RouteId` **before** it's
merged into the returned `FeedMessage` (i.e., before `Worker.cs` ever sees it) — same seam
`ApplyRailRouteIdMap` already uses, so both can run in sequence (`ApplyRailRouteIdMap` first if a
future city ever needed both a static map *and* a transform pipeline, though NYC bus only needs
the pipeline):

```csharp
void ApplyRouteIdNormalization(FeedMessage feed)
{
    if (config.RouteIdNormalization is not { Length: > 0 }) return;

    foreach (var entity in feed.Entities)
    {
        if (entity.Vehicle?.Trip?.RouteId is not null)
            entity.Vehicle.Trip.RouteId = RouteIdNormalizer.Apply(entity.Vehicle.Trip.RouteId, config.RouteIdNormalization);
    }
}
```

Called from `FetchVehiclesAsync` right alongside the existing `ApplyRailRouteIdMap(merged);` call.
This keeps `GtfsRtCity` a thin orchestrator and `RouteIdNormalizer` a pure, independently unit-
testable function (`Apply("Q06", [...]) == "Q6"`, `Apply("M15+", [...]) == "M15-SBS"`,
`Apply("bx3", ["uppercase"]) == "BX3"`, etc. — no HTTP, no worker, no city needed to test it).

## 6. RT fan-out

One feed, not eight (unlike subway's 8 line groups): `gtfsrt.prod.obanyc.com/vehiclePositions?key=<KEY>`
is a single citywide protobuf covering all boroughs + both operators. `GtfsRtCity`'s existing
per-URL try/catch loop handles this with `GtfsRtUrls: ["https://gtfsrt.prod.obanyc.com/vehiclePositions"]`
(the `?key=` query param appended the same way `ApiKeyEnvVar` already does for any city — see
`GtfsRtCity.FetchFeedAsync`'s existing `apiKey is not null ? $"{url}?api_key={apiKey}" : url`
pattern; confirm the exact query-param name the obanyc feed expects is `key`, not `api_key`,
before shipping — the feed evaluation's manual curl used `?key=<KEY>` explicitly, so `GtfsRtCity`'s
current hardcoded `?api_key=` string would need a per-city-configurable param name, OR the URL can
just be pre-templated with `?key=` in `GtfsRtUrls` config directly and `ApiKeyEnvVar` left unset —
simplest fix, no code change, at the cost of the key living in a URL string in
`GtfsRtCity.FetchFeedAsync`'s no-op branch instead of substituted in; revisit if this friction turns
out to matter).

## 7. Telemetry

**Decision: `EmitsTelemetry: true`** for `nymta-bus`. Unlike subway (`EmitsTelemetry: false`,
because subway positions are *synthesized*, not observed, and the multi-city decision explicitly
wanted telemetry to stay meaningful/comparable across cities rather than growing a
synthesized-data asterisk), bus positions are ordinary live GPS — structurally identical to MARTA's
bus feed or WMATA's, both already `EmitsTelemetry: true`. There's no principled reason to exclude
it, and doing so would be the one telemetry gap among all bus-equipped cities.

## 8. Registration & config

No new `ITransitCity` implementation, no new `Program.cs` branch — `nymta-bus` falls into the
*existing* `else` arm of the city-registry factory (`cities.Add(new GtfsRtCity(cfg, httpFactory,
logFactory.CreateLogger<GtfsRtCity>()));`), the same path WMATA and MBTA already take. This is the
practical payoff of choosing (A) in §2: this feature needs **zero** `Program.cs` changes.

`appsettings.json` (Worker + WebAPI) — a new `Cities:` entry:

```jsonc
{
  "Name": "nymta-bus",
  "GtfsRtUrls": [ "https://gtfsrt.prod.obanyc.com/vehiclePositions" ],
  "StaticZipUrls": [
    "http://web.mta.info/developers/data/nyct/bus/google_transit_manhattan.zip",
    "http://web.mta.info/developers/data/nyct/bus/google_transit_bronx.zip",
    "http://web.mta.info/developers/data/nyct/bus/google_transit_brooklyn.zip",
    "http://web.mta.info/developers/data/nyct/bus/google_transit_queens.zip",
    "http://web.mta.info/developers/data/nyct/bus/google_transit_staten_island.zip",
    "http://web.mta.info/developers/data/busco/google_transit.zip"
  ],
  "ApiKeyEnvVar": "NYMTA_BUS_API_KEY",
  "RouteIdNormalization": [ "uppercase", "plusToSbs", "stripLeadingZeros" ],
  "EmitsTelemetry": true
}
```

`CityNames.NymtaBus = "nymta-bus"` — new constant, `Shared/CityNames.cs`.

## 9. Edge cases & failure modes

| Case | Handling |
|------|----------|
| `NYMTA_BUS_API_KEY` unset/invalid | `GtfsRtCity.FetchFeedAsync` already logs a warning and returns `null` on non-success status — same as any misconfigured city; feed is empty for that tick, next tick retries |
| Route ID matches no static route even after normalization | Existing `Worker.cs` `skippedUnknownRoute` counter absorbs it — no NYC-specific handling needed |
| `RouteIdNormalization` config has a typo'd step name | `RouteIdNormalizer.ApplyStep`'s default arm is a no-op passthrough, never throws — a bad config entry degrades match rate, doesn't crash the tick |
| One of the 6 static zips 404s/fails | Existing per-zip try/catch in `BuildCityShapeSetAsync` already logs and continues with whatever zips succeeded (same as MARTA/WMATA today) |
| MTA Bus Company zip temporarily unavailable | Bus Co-only routes (Q06 etc.) simply won't resolve that refresh cycle; NYCT-covered routes are unaffected — same last-good-wins policy as everything else in `GtfsStaticLoader` |

## 10. Scope boundary

This doc is **bus only**. Subway (`nymta`) is untouched — no changes to `NymtaCity`,
`ShapeInterpolator`, `StopOffsetTable`, or the `/gtfs/subway/stop-offsets` endpoint.
`Worker.cs` stays unchanged (same as 040 — the whole point of the `ITransitCity`/`GtfsRtCity`
seam). SIRI JSON (`bustime.mta.info`) is out of scope — the `obanyc` protobuf path already matches
every other city's decode format and needs no new client code.

## 11. Effort summary

| Piece | Where | Size |
|-------|-------|------|
| `RouteIdNormalizer` + config field + `GtfsRtCity` invocation | new file + `CityConfig.cs` + `GtfsRtCity.cs` | **Small** — the one genuinely new piece, but pure/unit-testable |
| `appsettings.json` `nymta-bus` entry (Worker + WebAPI, + Development variants) | config only | Trivial |
| `CityNames.NymtaBus` constant | `Shared/CityNames.cs` | Trivial |
| Frontend: second `CityFab` picker entry ("New York Buses") | `CityFab.razor` (+ resx copy if audio/info overlays want bus-specific text) | Small |
| `NYMTA_BUS_API_KEY` registration + env var | ops/secrets, not code | Trivial (blocked on you obtaining a key) |

**Ballpark: under a day**, almost entirely config + one small pure-function class + its unit tests.
Structurally this is the cheapest city-onboarding path the project has (bus is `COMPATIBLE` per the
feed evaluation) — the only reason it isn't *zero* code is the route-ID mismatch, which is exactly
what `RouteIdNormalizer` exists to close.
