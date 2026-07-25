# Contract: Lean Output — `neighborhood_routes.json`

The default file consumed by skills. Must stay small enough for LLM context (SC-006).

## Top-level shape

```jsonc
{
  "generatedAt": "<UTC ISO-8601 string>",   // run timestamp (FR-010)
  "sourceGeoJson": "<basename of --geojson>", // (FR-010)
  "neighborhoods": [ <LeanNeighborhood>, ... ] // sorted by name asc (FR-006)
}
```

## `LeanNeighborhood`

| Field | Type | Rule |
|-------|------|------|
| `objectId` | int | `OBJECTID_1`; join key into the full file (FR-012). Required, non-null. |
| `name` | string | `NAME`. Required, non-null. |
| `npu` | string \| null | `NPU`. |
| `sqMiles` | number \| null | `SQMILES`. |
| `population` | int \| null | rounded to nearest int (FR-007); `null` if missing (FR-011). |
| `medianHouseholdIncome` | int \| null | rounded to nearest int; `null` if missing. |
| `transitCommutePercent` | number \| null | 1 decimal place; `null` if missing. |
| `carAlonePercent` | number \| null | 1 decimal place; `null` if missing. |
| `workFromHomePercent` | number \| null | 1 decimal place; `null` if missing. |
| `routes` | array of `LeanRoute` | matched routes; `[]` when none (FR-006). |

## `LeanRoute`

| Field | Type | Rule |
|-------|------|------|
| `routeId` | string | GTFS static id, e.g. `"26922"`. |
| `routeShortName` | string | human label, e.g. `"39"`. |

## Invariants (testable — SC-001/002/007)

- Exactly one entry per source neighborhood; none omitted.
- `neighborhoods` sorted ascending by `name`.
- No geometry/coordinate fields anywhere (FR-009).
- Every percentage with a value has ≤ 1 decimal place; `population`/`medianHouseholdIncome` are integers or `null`.
- Every `objectId` resolves to a key in the full file (FR-012).
- Missing numerics are `null`, never `0`.
