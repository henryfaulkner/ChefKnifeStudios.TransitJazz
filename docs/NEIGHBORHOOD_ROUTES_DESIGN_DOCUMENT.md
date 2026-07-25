# Neighborhood Routes — Full Design Document

> **Purpose of this document.** This is a self-contained, context-free specification for implementing
> the **neighborhood-routes** offline pre-computation tool and its downstream consumers. An Opus agent
> with no prior knowledge of this codebase should be able to implement the feature end-to-end by
> following this document verbatim.

---

## 1. Feature Overview

Feature `018-neighborhood-routes` is a **developer/analyst tool** — not a user-facing feature. It
answers the question: *which MARTA bus routes serve which Atlanta neighborhoods, and what is the
transit character of each neighborhood?*

The deliverable is a Python script (`tools/neighborhood-routes/generate.py`) that performs an
**offline spatial join** between:

1. **248 official Atlanta neighborhood polygons** from a GeoJSON file (City of Atlanta, 2024 demographic data)
2. **86 MARTA bus route LineStrings** fetched from the MartaJazz API (`/gtfs/routes/shapes`)

The output is two committed JSON files:

| File | Purpose |
|---|---|
| `tools/neighborhood-routes/neighborhood_routes.json` | Lean schema — fast context loading for skills |
| `tools/neighborhood-routes/neighborhood_routes_full.json` | Full demographic dump — deep-dive reference |

These files are consumed by:
- The `mj-data-explorer` skill (via a new context file)
- The `create-neighborhood-blurb` skill (reads lean file, drafts blurb copy)

**This is not a real-time system.** The script is re-run manually whenever GTFS route shapes change
(infrequent). No parquet schema changes. No new MCP tool (future iteration). No in-app UI.

---

## 2. Input Data

### 2.1 Neighborhood GeoJSON

**Source file:** `Official_Neighborhoods_with_Current_Demographic_Data_(2024).geojson`  
**Obtained from:** City of Atlanta open data portal (2024)  
**Passed to script as:** CLI argument `--geojson` (default: the Downloads path used during development)

**Key facts:**
- 248 features, geometry types: `Polygon` and `MultiPolygon`
- 25 NPUs (Neighborhood Planning Units): A–Z, skipping U
- 242 of 248 features have `HasData = true` (demographic data present)
- All coordinates in WGS84 (lon, lat)

**Properties used in lean schema:**

| GeoJSON property | Lean field | Notes |
|---|---|---|
| `OBJECTID_1` | `objectId` | Primary key — stable integer, used as join key |
| `NAME` | `name` | Neighborhood name, e.g. `"Old Fourth Ward"` |
| `NPU` | `npu` | Planning unit letter, e.g. `"M"` |
| `SQMILES` | `sqMiles` | Area in square miles |
| `population` | `population` | 2024 population estimate |
| `householdi` | `medianHouseholdIncome` | Median household income (USD) |
| `commute__3` | `transitCommutePercent` | % of workers commuting by transit |
| `commute__1` | `carAlonePercent` | % of workers driving alone |
| `commute__5` | `workFromHomePercent` | % of workers working from home |

**All remaining properties** (race/ethnicity, housing units, home value, education, age breakdowns,
etc.) go into the full dump verbatim under their original GeoJSON property names.

**Geometry is excluded from both output files.** The source GeoJSON is the geometry source of truth.

### 2.2 Route Shapes API

**Endpoint:** `GET https://marta-jazz-dev-ca-server.jollytree-dd5ca774.eastus2.azurecontainerapps.io/gtfs/routes/shapes`  
**Response:** GeoJSON `FeatureCollection`-style array of `RouteShapeFeature` objects

**RouteShapeFeature schema:**
```json
{
  "type": "Feature",
  "geometry": {
    "type": "LineString",
    "coordinates": [[-84.28052, 33.90255], ...]
  },
  "properties": {
    "routeId": "26922",
    "routeShortName": "39",
    "color": "#FF6600",
    "textColor": "#FFFFFF"
  }
}
```

**Key facts:**
- 86 routes as of 2026-06-14
- Coordinates are `[longitude, latitude]` pairs (WGS84)
- `routeId` is the internal GTFS ID (e.g. `"26922"`); `routeShortName` is the human label (e.g. `"39"`)

---

## 3. Output Files

### 3.1 Lean File: `neighborhood_routes.json`

**Loaded by default** by the `mj-data-explorer` skill and `create-neighborhood-blurb` skill.
Intentionally small — safe to include in LLM context without hitting token limits.

**Schema:**
```json
{
  "generatedAt": "2026-06-14T00:00:00Z",
  "sourceGeoJson": "Official_Neighborhoods_with_Current_Demographic_Data_(2024).geojson",
  "neighborhoods": [
    {
      "objectId": 13,
      "name": "Ridgewood Heights",
      "npu": "C",
      "sqMiles": 0.42,
      "population": 2191,
      "medianHouseholdIncome": 87000,
      "transitCommutePercent": 9.1,
      "carAlonePercent": 30.2,
      "workFromHomePercent": 58.7,
      "routes": [
        { "routeId": "26922", "routeShortName": "39" }
      ]
    }
  ]
}
```

**Notes:**
- `neighborhoods` is a flat array sorted by `name` ascending
- `routes` is the list of routes whose LineString intersects the neighborhood polygon; empty array `[]` if none
- Numeric fields are rounded: percentages to 1 decimal place, income/population to nearest integer
- `objectId` is the stable join key into `neighborhood_routes_full.json`

### 3.2 Full Dump: `neighborhood_routes_full.json`

**Loaded explicitly** only when deep demographic detail is needed. Should NOT be loaded into LLM
context by default — it contains ~130 fields per neighborhood.

**Schema:**
```json
{
  "generatedAt": "2026-06-14T00:00:00Z",
  "sourceGeoJson": "Official_Neighborhoods_with_Current_Demographic_Data_(2024).geojson",
  "neighborhoods": {
    "13": {
      "OBJECTID_1": 13,
      "NAME": "Ridgewood Heights",
      "NPU": "C",
      "GEOTYPE": "Neighborhood",
      "ACRES": 268.5,
      "SQMILES": 0.42,
      "population": 2.191,
      "commute_AC": 9.06,
      "commute__1": 30.2,
      "commute__2": 0.0,
      "commute__3": 0.0,
      "commute__4": 0.0,
      "commute__5": 58.72,
      "commute__6": 2.01,
      "householdi": 87000,
      "homevalue_": 425000,
      "HasData": true,
      "...": "all remaining GeoJSON properties verbatim"
    }
  }
}
```

**Notes:**
- `neighborhoods` is a dictionary keyed by `objectId` as a **string** (e.g. `"13"`)
- All GeoJSON property values are stored as-is (no renaming, no rounding)
- Geometry (`coordinates`) is excluded
- To look up a neighborhood's full record from a lean entry: `full["neighborhoods"][str(lean["objectId"])]`

---

## 4. Script: `tools/neighborhood-routes/generate.py`

### 4.1 Dependencies

```
shapely>=2.0
requests>=2.28
```

Add a `requirements.txt` alongside the script with these two packages.

### 4.2 CLI Interface

```
python generate.py [--geojson PATH] [--api BASE_URL] [--out-dir DIR]
```

| Argument | Default | Description |
|---|---|---|
| `--geojson` | `~/Downloads/Official_Neighborhoods_with_Current_Demographic_Data_(2024).geojson` | Path to source GeoJSON |
| `--api` | `https://marta-jazz-dev-ca-server.jollytree-dd5ca774.eastus2.azurecontainerapps.io` | MJ API base URL |
| `--out-dir` | `.` (same directory as script) | Where to write output JSON files |

### 4.3 Algorithm

```
1. Load GeoJSON → list of (objectId, name, properties, shapely_geometry)
   - For MultiPolygon: use shapely.geometry.shape() directly (handles both Polygon + MultiPolygon)

2. Fetch route shapes → GET {api}/gtfs/routes/shapes
   - Parse response as JSON array
   - For each feature: build shapely.geometry.LineString from geometry.coordinates
   - Store as list of (routeId, routeShortName, linestring)

3. Spatial join:
   For each neighborhood polygon:
     matched_routes = [r for r in routes if polygon.intersects(r.linestring)]
   
   Note: shapely.intersects() returns True if any part of the LineString touches or
   crosses the polygon boundary or interior. This is the correct semantic for
   "route serves neighborhood."

4. Build lean entries:
   - Round percentages to 1 decimal place
   - Round income/population to nearest integer
   - Sort neighborhoods array by name ascending

5. Build full entries:
   - Copy all GeoJSON properties verbatim (exclude geometry coordinates)
   - Key by str(objectId)

6. Write neighborhood_routes.json (lean)
7. Write neighborhood_routes_full.json (full)

8. Print summary:
   - Total neighborhoods processed
   - Neighborhoods with ≥1 route matched
   - Neighborhoods with 0 routes matched (list their names — likely edge/rural areas)
   - Total unique routes matched across all neighborhoods
```

### 4.4 Edge Cases

- **MultiPolygon neighborhoods:** `shapely.geometry.shape()` handles both Polygon and MultiPolygon transparently — no special case needed.
- **Null/missing demographic fields:** Store `null` in JSON (do not default to 0 — a 0 income is misleading). The lean schema should use `null` for missing numeric fields.
- **Neighborhoods with no matched routes:** Include in output with `"routes": []`. Log their names to stdout as a warning.
- **API failure:** If the route shapes API is unreachable, exit with a clear error message and non-zero exit code. Do not write partial output.

---

## 5. Commute Field Decoder

The GeoJSON commute fields have opaque names. Based on ACS commute mode order, the mapping is:

| GeoJSON field | Meaning |
|---|---|
| `commute_AC` | Total workers (used as denominator context) |
| `commute__1` | Car, truck, or van — drove alone |
| `commute__2` | Car, truck, or van — carpooled |
| `commute__3` | Public transportation (transit) |
| `commute__4` | Walked |
| `commute__5` | Worked from home |
| `commute__6` | Other means |

Values are **percentages** (0–100), not raw counts. `commute_AC` appears to be a percentage of the
population that are workers (not a raw count either — verify against known neighborhoods if in doubt).

---

## 6. Skill Updates

### 6.1 `mj-data-explorer` Context File

**Create:** `.claude/skills/mj-data-explorer/neighborhood_routes_context.md`

This file is loaded as part of the `mj-data-explorer` skill context. It should instruct the skill to:

1. Know that `tools/neighborhood-routes/neighborhood_routes.json` exists and contains the lean neighborhood-route mapping
2. Read the lean file directly (via the Read tool) when the analyst asks neighborhood-level questions (e.g., "which routes serve Vine City?", "what neighborhoods does route 39 pass through?", "which neighborhoods have the highest transit commute rate?")
3. Read `tools/neighborhood-routes/neighborhood_routes_full.json` only when the analyst explicitly asks for detailed demographic data on a specific neighborhood — and only look up the specific `objectId` entry, not the whole file
4. Never load the full dump speculatively — only on explicit analyst request

**Content outline for the context file:**
```markdown
## Neighborhood Routes Data

A static pre-computed dataset lives at `tools/neighborhood-routes/neighborhood_routes.json`.
It maps each of Atlanta's 248 official neighborhoods to the MARTA bus routes that intersect it,
plus key demographic signals (population, income, transit/car/WFH commute rates).

Load this file when the analyst asks about:
- Which routes serve a neighborhood
- Which neighborhoods a route passes through
- Transit dependency rankings across neighborhoods
- Demographic context for blurb authoring

For detailed demographic fields (race, education, housing, home value, age breakdowns), load
`tools/neighborhood-routes/neighborhood_routes_full.json` and look up by objectId.
Do NOT load the full dump unless the analyst explicitly asks for it.
```

### 6.2 `create-neighborhood-blurb` Skill

**Update** the existing `create-neighborhood-blurb` skill to:

1. Accept a neighborhood name or `objectId` as input
2. Read `tools/neighborhood-routes/neighborhood_routes.json`
3. Find the matching lean entry
4. Use the lean fields as structured input to draft blurb copy

**Target blurb shape** (mirrors existing `RouteBlurb` record pattern, scoped to neighborhood):
```csharp
public record NeighborhoodBlurb(
    int ObjectId,
    string Name,
    string ToneDescription,   // 1–2 sentences: character of the neighborhood + transit role
    string Significance,      // 1 sentence: why this neighborhood matters to the route network
    bool IsPlaceholder
);
```

**Blurb authoring signals** the skill should use:
- `routes` list — how many routes, which ones (reference by shortName)
- `transitCommutePercent` — high (>15%) = transit-dependent; low (<5%) = underserved or car-dominant
- `workFromHomePercent` — high (>40%) = low peak ridership potential
- `medianHouseholdIncome` — economic character context
- `npu` — planning unit grouping (can reference NPU in blurb for Atlanta-literate readers)
- `population` + `sqMiles` — density context

**Example output** for Vine City (NPU-L, income $37k, transit 0.5%, WFH 49%, routes TBD):
```
ToneDescription: "Vine City is a historic west-side neighborhood anchored by low median incomes
and a high work-from-home rate — unusual for a community with limited broadband access, suggesting
informal or gig-economy work patterns. MARTA routes here serve essential trips, not commuter choice."

Significance: "As one of Atlanta's most economically vulnerable neighborhoods, Vine City routes
carry riders with few alternatives — frequency and reliability matter more here than anywhere."
```

---

## 7. File Layout

```
tools/
  neighborhood-routes/
    generate.py                      ← pre-computation script
    requirements.txt                 ← shapely, requests
    neighborhood_routes.json         ← lean output (committed)
    neighborhood_routes_full.json    ← full dump output (committed)

.claude/
  skills/
    mj-data-explorer/
      neighborhood_routes_context.md ← new context file for the skill
    create-neighborhood-blurb/
      (existing skill files, updated)
```

---

## 8. Re-generation Trigger

Re-run `generate.py` when:
- MARTA GTFS route shapes change (new routes added, existing routes modified)
- The neighborhood GeoJSON source file is updated by the City of Atlanta

Re-generation is manual. There is no scheduled job. A comment at the top of `generate.py` notes this.

The output files are committed to the repo alongside the script so that consumers (skills, analysts)
never need to run the script themselves — they read the pre-built JSON directly.

---

## 9. Out of Scope

- Parquet schema changes — neighborhood enrichment is replaced by the static JSON
- New MCP tool wrapper — future iteration; analysts use Claude + file reads directly
- Scheduled or automated re-generation
- In-app UI (this is a developer/analyst tool only)
- Dark mode or language settings (separate features)
- Neighborhood polygons rendered on the live map (feature 011, separate branch)
