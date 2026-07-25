# Contract: Full Output — `neighborhood_routes_full.json`

The deep-dive reference. NOT loaded into LLM context by default — consulted per-`objectId` only on explicit request (FR-016).

## Top-level shape

```jsonc
{
  "generatedAt": "<UTC ISO-8601 string>",      // (FR-010)
  "sourceGeoJson": "<basename of --geojson>",  // (FR-010)
  "neighborhoods": {                            // dict keyed by str(objectId) (FR-008)
    "<objectId-as-string>": { <all source properties verbatim> }
  }
}
```

## Per-neighborhood record

- Key: `str(objectId)` (e.g. `"13"`).
- Value: every property from the GeoJSON feature's `properties`, **verbatim** — original names, original values, **no rounding** (FR-008).
- Geometry (`coordinates`) is **excluded** (FR-009).
- ~130 fields per neighborhood (race/ethnicity, housing, home value, education, age, all `commute__*`, etc.).

## Invariants (testable — SC-003)

- `neighborhoods` is a JSON object (dict), not an array.
- Keys are strings.
- For every lean entry: `full["neighborhoods"][str(lean["objectId"])]` exists and its `OBJECTID_1` equals `lean["objectId"]`.
- No geometry/coordinate fields present.
- Values are unmodified from source (a value rounded in the lean file appears at full precision here).
