# City Onboarding Integration-Test Framework — Design Document

**Feature:** 037-city-integration-test-framework
**Status:** Design
**Author:** (design doc)
**Goal:** Every city we add (marta, wmata, mbta, …future) is guarded by a
uniform, data-driven integration-test suite that proves the GTFS pipeline
produces **well-formatted output** — from raw upstream feed through the
`/gtfs/routes/shapes` payload the worker consumes and the SignalR batch it
publishes.

---

## 1. Problem

Adding a city today (feature 031 multi-city, 032 MBTA) means wiring a new
`Cities[]` entry plus, in practice, discovering that city's quirks by hand:

- MBTA's `routes.txt` has a `route_url` column that looked like a color and
  poisoned `route_color` until the CSV quoting bug was fixed (see
  `GtfsStaticLoaderTests.SplitCsvLine_QuotedFieldWithComma_TreatedAsSingleField`).
- WMATA rail route ids arrive as `BLUE`/`BLUE0` in GTFS-RT but the static feed
  keys on `B` — handled by `RailRouteIdMap`, but nothing *asserts* the map is
  complete or that mapped ids actually resolve against the static route set.
- Each agency uses a different `routes.txt` / `shapes.txt` column order, has
  different coordinate bounds, different `route_type` conventions, and some ship
  multiple zips per city.

These are all "malformed output" classes: a color that's actually a URL, a
route whose shape is empty, coordinates in the wrong hemisphere, a GTFS-RT
vehicle whose `route_id` can never join to a static route. The current tests
(`GtfsStaticLoaderTests`) cover the *parser* well but are hand-authored
per-fixture and don't cover the **end-to-end contract** or force each new city
through the same gauntlet.

**The framework's job:** make "is this city's output well-formed?" a single,
repeatable, per-city test matrix that fails loudly the moment a new city
violates a formatting invariant — ideally in CI, before it ships.

---

## 2. What "well-formatted output" means (the contract)

The pipeline has three output surfaces. The framework asserts invariants at each.

```
 upstream GTFS zip                     upstream GTFS-RT .pb
        │                                      │
        ▼                                      ▼
 GtfsStaticLoader  ──►  KV store  ──► /gtfs/routes/shapes  ──► Worker.BuildRouteIndex
 (parse + simplify)     (per-city      (RouteShapeFeature[])    (RoutePoint index,
                         "city:route")                           trigger points)
                                                                        │
                                                                        ▼
                                                          GtfsRtCity.FetchVehiclesAsync
                                                          + spatial reconciliation
                                                                        │
                                                                        ▼
                                                     RouteNearestPointBatchEvent (SignalR)
```

### Surface A — Static route shapes (`RouteShapeFeature`)

The unit each city contributes to `/gtfs/routes/shapes`. Well-formed means, for
every feature a city produces:

| Invariant | Rule |
|-----------|------|
| **A1 Type** | `Type == "Feature"`, `Geometry.Type == "LineString"`. |
| **A2 Non-empty geometry** | `Geometry.Coordinates.Length >= 2` (a line needs ≥2 points). |
| **A3 Coordinate order** | each coord is `[lon, lat]`, both finite doubles. |
| **A4 Coordinates in city bounds** | every `(lat, lon)` falls inside the city's declared bounding box (catches lon/lat swap, wrong hemisphere, garbage rows). |
| **A5 Color format** | `Color`/`TextColor` are `null` **or** match `^#[0-9A-F]{3}([0-9A-F]{3})?$` (never a URL — the MBTA bug). |
| **A6 RouteId present** | `Properties.RouteId` non-empty. |
| **A7 City stamped** | `Properties.City == <expected city name>` (lowercase). |
| **A8 Mode valid** | `Properties.Mode` is a defined `TransitMode` enum value. |
| **A9 Join key present** | `RouteShortName ?? RouteId` (the worker's index key, `Worker.cs:103`) is non-empty. |

### Surface B — GTFS-RT ↔ static join integrity

The worker drops any vehicle whose `route_id` isn't in the static index
(`skippedUnknownRoute`, `Worker.cs:255`). A city where most vehicles skip is
"malformed" from the app's perspective — it produces silence. So:

| Invariant | Rule |
|-----------|------|
| **B1 Join yield** | Given a live (or recorded) GTFS-RT sample, ≥ *threshold* of vehicles with a non-empty `route_id` resolve against the static index (after `RailRouteIdMap`). Default threshold: **80 %** (configurable per city). |
| **B2 RailRouteIdMap closure** | every value in `RailRouteIdMap` resolves to an existing static route id; every mapped-away key is one actually seen in the RT feed (no dead map entries). |
| **B3 Vehicle position sanity** | every RT vehicle with a position has finite lat/lon inside the city bounds. |

### Surface C — Published batch (`RouteNearestPointBatchEvent`)

The end product the client hears. Well-formed means:

| Invariant | Rule |
|-----------|------|
| **C1 Snap distance** | each record's snapped point is within *N* meters of the raw point (a snap that lands kilometers away = wrong route index or coordinate swap). Default 500 m. |
| **C2 Records reference known routes** | every `RouteId` in the batch exists in the static index. |
| **C3 Mode consistency** | record `Mode` equals the static route's mode. |
| **C4 No NaN/Inf** | all lat/lon/speed/bearing fields are finite. |

---

## 3. Design principles

Aligned with the repo constitution (Principle VII "never re-fetch", the
ponytail "laziest thing that works" bias visible throughout `GtfsStaticLoader`):

1. **Data-driven, one row per city.** A single `CityContract` record per city
   drives the whole matrix. Adding a city = add one contract + one frozen
   fixture. No new test *methods*.
2. **Two tiers, sharply separated:**
   - **Tier 1 — Offline (deterministic, runs in CI on every PR).** Feeds
     *frozen fixtures* (a trimmed real zip + a recorded `.pb`) through the real
     parsing/index/snap code. No network. This is the gate.
   - **Tier 2 — Live smoke (opt-in, network, nightly / manual).** Hits the real
     upstream URLs for each city and re-runs the *same* invariant assertions on
     live data. Catches "the agency changed their feed" drift. Skipped by
     default via a trait so CI stays hermetic and fast.
3. **Reuse the real code paths.** Tests call `GtfsStaticLoader.ParseRouteMetadata`,
   `ParseShapes`, `BuildLineStringFeature`, `Worker.BuildRouteIndex`,
   `GtfsRtCity` (with a stub `HttpClient`), and `RouteSnapper` — **not**
   reimplementations. A framework that reimplements the pipeline tests nothing.
4. **Fixtures are frozen and tiny.** Trim each real feed to a handful of routes
   (one bus, one rail, one "tricky" route per city) and check the bytes into the
   repo. Deterministic, fast, reviewable, offline. Regenerated by an explicit
   script, never at test time.
5. **Fail loud, name the city.** Every assertion message includes the city name
   and route id so a red build says *which* city broke *which* invariant.

---

## 4. Architecture

### 4.1 The `CityContract` (the one row per city)

Lives in the test project (or a small shared test-support assembly). Declares
everything the matrix needs to know about a city.

```csharp
public sealed record CityContract
{
    public required string CityName { get; init; }          // "mbta"
    public required GeoBounds Bounds { get; init; }          // A4/B3 bounding box
    public required string StaticZipFixture { get; init; }   // path to frozen trimmed .zip
    public string? GtfsRtFixture { get; init; }              // path to frozen .pb (Tier 1 join test)

    // Live-tier only (Tier 2): pulled from the same appsettings Cities[] block
    public string[] StaticZipUrls { get; init; } = [];
    public string[] GtfsRtUrls { get; init; } = [];
    public string? ApiKeyEnvVar { get; init; }
    public IReadOnlyDictionary<string, string>? RailRouteIdMap { get; init; }

    // Tunable thresholds (defaults in §2)
    public double MinJoinYield { get; init; } = 0.80;        // B1
    public double MaxSnapMeters { get; init; } = 500;        // C1

    // Expected sentinels — the "tricky rows" we insist stay correct.
    public IReadOnlyList<ExpectedRoute> MustContain { get; init; } = [];
}

public sealed record ExpectedRoute(
    string RouteId,
    string? ExpectedColor,      // e.g. MBTA 47 → "#FFC72C", never the URL
    TransitMode ExpectedMode);

public readonly record struct GeoBounds(
    double MinLat, double MaxLat, double MinLon, double MaxLon)
{
    public bool Contains(double lat, double lon) =>
        lat >= MinLat && lat <= MaxLat && lon >= MinLon && lon <= MaxLon;
}
```

**Contract registry** — a single static list all theories enumerate:

```csharp
public static class CityContracts
{
    public static readonly CityContract Marta = new() { CityName = "marta", /* … */ };
    public static readonly CityContract Wmata = new() { CityName = "wmata", /* … */ };
    public static readonly CityContract Mbta  = new() { CityName = "mbta",  /* … */ };

    public static IEnumerable<object[]> All() =>
        new[] { Marta, Wmata, Mbta }.Select(c => new object[] { c });
}
```

Every Tier-1 test is `[Theory][MemberData(nameof(CityContracts.All))]`. **Adding
a city means appending one `CityContract` and its fixtures — every invariant
test now runs against it automatically.** That is the whole point.

### 4.2 The assertion library (`CityOutputAssert`)

The invariants from §2, factored into reusable asserters so both tiers and all
cities share one implementation:

```csharp
public static class CityOutputAssert
{
    // Surface A — runs over the RouteShapeFeature[] a city produced
    public static void WellFormedShapes(CityContract c, IReadOnlyList<RouteShapeFeature> features);

    // Surface B — join yield + map closure
    public static void JoinIntegrity(CityContract c, FeedMessage rtFeed,
                                     IReadOnlyDictionary<string, RoutePoint[]> staticIndex);

    // Surface C — over a produced RouteNearestPointBatchEvent
    public static void WellFormedBatch(CityContract c, RouteNearestPointBatchEvent batch,
                                       IReadOnlyDictionary<string, RoutePoint[]> staticIndex);
}
```

Each method loops the relevant invariants and throws `Xunit` assertions with
city-tagged messages, e.g.
`Assert.True(color is null || HexColor.IsMatch(color), $"[{c.CityName}] route {routeId} color '{color}' is not a hex color (A5)");`

### 4.3 The fixture pipeline

```
tools/gtfs-fixture-trimmer/          (new, small dotnet console or script)
   └─ downloads a city's real zip/.pb, keeps ~3 routes + their shapes/trips,
      re-zips, writes to:
specs/037-.../fixtures/<city>/static.zip
specs/037-.../fixtures/<city>/vehicles.pb
```

- **Trimming rule:** keep the routes named in `CityContract.MustContain` plus
  one extra bus and one rail route, and only the `shapes`/`trips` rows they
  reference. Keeps fixtures < ~50 KB.
- **Regeneration is explicit and rare** — run the trimmer when an agency changes
  its schema, review the byte diff, commit. Never fetched at test time
  (Principle VII: tests never hit the network in Tier 1).
- Fixtures ship as embedded resources or `CopyToOutputDirectory` content in the
  test csproj.

### 4.4 Test project layout

Extends the existing `ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests` (static +
endpoints) and `…TransitDataWorker.Tests` (index + snap), keeping the split that
already exists rather than inventing a third project:

```
WebAPI.Tests/
  CityContracts.cs                 ← the registry (shared via linked file or InternalsVisibleTo)
  Framework/
    CityContract.cs
    GeoBounds.cs
    CityOutputAssert.cs
    FixtureLoader.cs               ← unzips a fixture into a ZipArchive / FeedMessage
  Cities/
    StaticShapeContractTests.cs    ← Surface A (Tier 1)
    LiveFeedContractTests.cs       ← Surface A + B (Tier 2, [Trait("tier","live")])
TransitDataWorker.Tests/
  Cities/
    JoinIntegrityTests.cs          ← Surface B (Tier 1, uses frozen .pb + frozen index)
    BatchOutputContractTests.cs    ← Surface C (Tier 1, drives real reconciliation once)
```

`CityContracts` + `Framework/*` are shared by both test projects via a linked
source file or a tiny `…Tests.Shared` support library (whichever the team
prefers; linked file is the lazier default).

### 4.5 Running the tiers

- **Tier 1 (default / CI):** `dotnet test` — offline, deterministic, part of the
  existing GitHub Actions job (feature 010). No secrets, no network.
- **Tier 2 (live):** `dotnet test --filter tier=live` — reads real URLs from the
  same `Cities[]` config, injects `WMATA_API_KEY` etc. Runs nightly (scheduled
  workflow) and on-demand. Failures here mean *upstream drift*, not our
  regression — triaged, not necessarily merge-blocking.

---

## 5. How a new city gets onboarded (the payoff)

1. Add the `Cities[]` block in `appsettings.json` (existing step).
2. Add one `CityContract` to `CityContracts` with the city's bounding box,
   `MinJoinYield`, and 2–3 `MustContain` sentinel routes (the "tricky" ones).
3. Run `tools/gtfs-fixture-trimmer <city>` to freeze `static.zip` + `vehicles.pb`.
4. `dotnet test`. The entire Surface A/B/C matrix now runs against the new city.
   Red = a formatting invariant the city violates; green = safe to ship.

No new test methods. No copy-pasted assertions. One row, one fixture, done.

---

## 6. Non-goals

- Not a load/perf test (feature 022 owns render perf).
- Not asserting *musical* output (Tone.js voices) — that's client-side and
  subjective; the framework stops at the SignalR batch contract.
- Not validating the telemetry parquet datasets (feature 013/014 own those; the
  MCP bridge already has its own accept/reject vectors).
- Tier 2 does not gate merges — upstream agencies change on their own schedule.

---

See `test-scenarios.md` for the full enumerated scenario catalog (the actual
`[Fact]`/`[Theory]` list the framework ships).
