# Contract: Go allow-list validator (`tools/telemetry-mcp/internal/validate/validate.go`)

The three per-dataset column maps collapse to **one merged map** for the single `telemetry` dataset. Tokenizer/parser/kinds and all forbidden-keyword/char/URL checks are **unchanged** (FR-028) — only `validDatasets`, `datasetColumns`, and three error strings change.

## `validDatasets` (was 3 entries)

```go
var validDatasets = map[string]bool{
    "telemetry": true,
}
```

## `datasetColumns` (was 3 maps → one merged map)

```go
var datasetColumns = map[string]map[string]valueKind{
    "telemetry": {
        // common
        "event_type":                     kindString,
        "event_id":                       kindString,
        "observation_utc":                kindTimestamp,
        // per-city only
        "city_name":                      kindString,
        "feed_freshness_seconds":         kindNumeric,
        // full-cycle only
        "cities_processed_count":         kindNumeric,
        "cities_processed_csv":           kindString,
        // shared
        "time_taken_seconds":             kindNumeric,
        "health_ok":                      kindBool,
        "tones_emitted":                  kindNumeric,
        "vehicles_processed":             kindNumeric,
        "gc_heap_bytes":                  kindNumeric,
        "process_working_set_bytes":      kindNumeric,
        "vehicle_state_cache_size":       kindNumeric,
        "crossing_baseline_cache_size":   kindNumeric,
        "route_index_size":               kindNumeric,
        "route_trigger_point_cache_size": kindNumeric,
    },
}
```

17 columns — must exactly match contracts/telemetry-event-schema.md (same names, same kind classification where numeric ⊇ INT32/INT64/DOUBLE, `health_ok` is bool, `observation_utc` is timestamp).

## Error-string changes

Three literals that name `snap, lerp, cycle` are re-pointed to `telemetry`:
- `ValidateDataset` (currently `validate.go:138`): `must be one of snap, lerp, cycle` → `must be one of telemetry`.
- `Filter` fallback (currently `validate.go:161`): same substitution.
- Any test-visible message asserting the old triple.

## Unchanged (do NOT touch)

- `valueKind` enum + kind→literal type checking in `parseComparison` (lines 331-350).
- `forbiddenKeywords`, `forbiddenChars`, comment/URL/`..` checks, `MaxFilterLength`, `dateRegex`, `ValidateDate`.
- `tokenize`, `parseStringLiteral` (`[A-Za-z0-9 _-]` only), `parseNumber`, `parseIdentifier`, `isIdentifierChar` (no `.`).

## Accept / reject vectors (for `validate_test.go`)

All against dataset `telemetry`.

### Accept

| filter | why |
|---|---|
| `event_type = 'PerCityCycle'` | string discriminator, primary scoping filter (FR-027) |
| `event_type = 'FullCycle'` | idem |
| `health_ok = false` | bool column ↔ bare bool |
| `vehicles_processed > 0 AND health_ok = true` | numeric + bool, AND |
| `city_name = 'MARTA'` | string column |
| `tones_emitted >= 5 OR feed_freshness_seconds > 60` | numeric, OR |
| `observation_utc > '2026-07-11'` | timestamp ↔ date string |
| `(event_type = 'FullCycle' OR event_type = 'PerCityCycle') AND gc_heap_bytes > 100000000` | grouping + numeric |
| `route_index_size > 0 AND crossing_baseline_cache_size >= 0` | new numeric columns |

### Reject

| filter | why |
|---|---|
| dataset `snap` / `lerp` / `cycle` (any filter) | dataset no longer valid (FR-025) — `ValidateDataset` fails |
| `snap_distance_km > 0.5` | retired column → unknown (FR-004/FR-005) |
| `pos_delta_km > 1.0` | retired lerp column → unknown |
| `last_update_cache_size > 0` | dropped column → unknown (FR-020) |
| `health_ok = 1` | bool wants `true`/`false`, not a number |
| `health_ok = 'true'` | bool must be unquoted |
| `event_type = PerCityCycle` | string wants quotes |
| `tones_emitted = 'five'` | numeric wants a number |
| `observation_utc > '2026-07-11T00:00:00'` | `:`/`T` forbidden in strings |
| `event_type.value = 'x'` | dotted identifier → unknown column |
| `SELECT * FROM telemetry` | forbidden keyword |
| `event_type = 'x'; DROP TABLE t` | `;` forbidden char |
