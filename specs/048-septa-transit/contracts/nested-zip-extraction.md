# Contract: Nested-Zip Static GTFS Extraction

The one genuinely new piece of production logic this feature introduces, inside
`GtfsStaticLoader.BuildCityShapeSetAsync` (`Server/ChefKnifeStudios.MartaJazz.Server.WebAPI/GtfsStatic/GtfsStaticLoader.cs`).
City-agnostic — not a `septa`-specific branch.

## Behavior

For each `zipUrl` in a city's `StaticZipUrls`, after downloading and opening the outer
`ZipArchive`:

1. If the outer archive contains `trips.txt` at its root → process it directly (**today's
   behavior, unchanged** — this is the path every existing city takes).
2. Else, scan the outer archive's entries for `.zip`-suffixed names. If one or more exist:
   - Prefer an entry whose name does NOT contain `"rail"` (case-insensitive) — this is the
     bus/trolley/streetcar/NHSL archive for SEPTA (`google_bus.zip` over `google_rail.zip`).
   - Open the selected entry's stream as a nested `ZipArchive` and process **that** as the
     effective archive for steps that follow (route/shape/metadata parsing).
3. Else (no root `trips.txt` AND no nested zip entry found) → treat as a failed fetch for this
   URL: log a warning, contribute zero routes for this URL (existing per-URL try/catch already
   wraps this method's caller).

## Accept / reject vectors

| Scenario | Expected |
|----------|----------|
| Existing flat zip (MARTA, WMATA, MBTA, NYMTA, TTC) — `trips.txt` at root | Step 1 applies; behavior and output byte-identical to before this feature. |
| SEPTA's `gtfs_public.zip` — no root `trips.txt`, contains `google_bus.zip` + `google_rail.zip` | Step 2 applies; `google_bus.zip` (non-"rail" name) selected and unwrapped; routes/shapes extracted from it. |
| A hypothetical zip with no root `trips.txt` and only one nested zip, named ambiguously (no "rail" substring either way) | Step 2 applies; the single nested zip is selected (no tie to break). |
| A zip with no root `trips.txt` and NO nested zip entries at all (e.g. corrupted download, unexpected format change upstream) | Step 3 applies; 0 routes returned for this URL; existing `fresh.Count == 0` guard in `RefreshAllCitiesAsync` keeps last-known-good data for the city; warning logged; no exception escapes. |
| A zip with root `trips.txt` present AND (coincidentally) a nested `.zip` entry too | Step 1 applies — root wins; the nested entry is never opened. Prevents unnecessary/incorrect unwrapping for any future flat-zip city that happens to bundle an unrelated `.zip` file alongside its GTFS text files. |
| Selected nested zip is itself malformed/unreadable | Exception propagates to the existing per-`zipUrl` `try/catch` in `BuildCityShapeSetAsync`, logged, contributes zero routes for this URL — same failure contract as any other per-zip fetch/parse error today. |

## Non-goals

- No new `CityStaticEntry`/config field is introduced to specify or override which nested zip to
  pick — selection is purely structural (see research.md R1) since only one city needs this today.
- No recursive multi-level unwrapping (zip-of-zip-of-zips) — one level of nesting is all this
  contract handles; SEPTA's structure is exactly one level deep.
- No change to `ParseRouteToShapeMap`, `ParseShapes`, `ParseRouteMetadata`, or
  `BuildZipRouteFeatures` — they continue to operate on whatever `ZipArchive` they're handed,
  unaware of whether it was the outer or an unwrapped nested archive.
