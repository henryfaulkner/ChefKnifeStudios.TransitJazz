# Reporting query results

Answer the user's question first. Include enough provenance to assess the answer without repeating every raw database column. Never invent a row, environment, available category, collection start, or observed zero.

## Default display

Use a concise Markdown table with readable headings and units. Display UTC timestamps as ISO 8601 with `Z`; retain local date/hour, named zone, UTC offset, and UTC identity for local-time comparisons. Keep integer counts exact and booleans as true/false. Round display values as appropriate for the question, typically two decimals; retain raw numeric precision and denominators for calculations and exports.

Before or after the table, state:

- Selected environment/target and database/schema (nonsecret identifiers only).
- Requested UTC range `[from_utc, to_utc)`, city/category filters, definition version, cadence policy where applicable, and any assumed defaults.
- Tables or checked-in recipe used, and the SQL/parameters when the user requested SQL or needs a reproducible analysis.
- Actual contributing range, row count and display limit; disclose truncation. Do not truncate the coverage calculation or sum a displayed subset and present it as the full period.
- Coverage and exclusions relevant to the measure. For definitive category reports, include complete contributing UTC hours, expected/requested windows, missing/Partial/NoData/conflicting exclusions and reasons, and beginning of retained category capture.

For category means expose the numerator and its actual denominator. Add policy/version columns when multiple groups exist. If there are many exclusions, summarize counts by reason and show a small representative selection; keep the full report available when requested. Boundary context is labeled with whole-window bounds and requested intersection, never prorated.

Example layout (values below illustrate the contract; they are not live results):

| City | Category | Accepted meters | Observed vehicle-hours | Meters / observed vehicle-hour | Complete UTC hours |
| --- | --- | ---: | ---: | ---: | ---: |
| atlanta | bus | 3,000.00 | 5 | 600.00 | 2 |

The example combines two verified hours with 1,200/3 and 1,800/2: `3000 / 5 = 600`. Averaging their displayed hourly means would give a different, incorrectly weighted result.

## Nulls, zeros, and coverage

| Evidence | Display and interpretation |
| --- | --- |
| SQL NULL / zero denominator | `Unavailable` in prose/table; preserve null in machine output. Explain unknown/missing measure or empty denominator. |
| Valid sample with zero value | Display `0`; it is an observed zero. A complete zero-vehicle hour can have zero activity/cadence and null movement means. |
| No retained key | `Missing`; do not relabel as stored NoData or synthesize zeros. |
| Stored NoData | `NoData`; category counter zeros carry no eligible-measure evidence. |
| Partial | Label diagnostics Partial and exclude from definitive complete-hour measures. |
| Dashboard Discrepant | Label discrepancy; confirmed sampled values are preserved but should not be presented as reconciled data. |
| Category `has_conflict=true` | `Conflict`; exclude even if the immutable collection status says Complete. |
| Partial request boundary | Label whole-minute/hour context and its exact bounds; exclude from fully contained definitive windows. |

An empty result means no matching retained/contributing rows. It does not establish that the city's vehicles were inactive. Distinguish no category history, missing hours, disabled capture (only if verified), definition/filter mismatch, and incomplete/conflicting coverage. For a failed query, state the failure instead of presenting any earlier emitted result sets as a complete report.

Dashboard latest sampled counts, rates, and p95 estimates retain their meanings. Do not label `sum(vehicles_processed)` as the number of distinct vehicles or `sum(tones_emitted)` as actual notes heard. Do not compute a period p95 by averaging minute p95 estimates.

## Exports

Respect the requested JSON/CSV shape. Do not create an export file unless requested or useful for an explicitly requested report artifact.

- CSV: the helper's `-Format csv` returns headers, standard CSV quoting, raw decimals, and SQL-null marker `NULL`. State the marker. Export one result set per CSV file; multi-query measure/coverage output is a sequence of separate CSV result sets, not one CSV table. Actual text equal to the marker is ambiguous, so use JSON for lossless arbitrary text.
- JSON: prefer server-side `jsonb_build_object`, `to_jsonb`, and `jsonb_agg` around the selected query so SQL null becomes JSON null and booleans/numbers keep their types. An empty result list is `[]`. Preserve decimal accuracy if a downstream consumer uses binary floating-point numbers.
- For a combined JSON report use `requested`, `source`, `definition`, `coverage`, and `rows` fields when the user has not specified a schema. Include numerator/denominator, exact contributing windows or bounds, `first_collectible_utc`, and exclusions. Use explicit RFC 3339 formatting for timestamp strings.
- Machine exports retain all requested rows. A conversational display limit does not silently limit an export or its summary.

Source contract for category response semantics: `specs/056-city-transit-type-insights/contracts/observed-category-statistics-v1.md`. Recipe-specific coverage definitions and exclusion reason codes are described in [queries.md](queries.md).
