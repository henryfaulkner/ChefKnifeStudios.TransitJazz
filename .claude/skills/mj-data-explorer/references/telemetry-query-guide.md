<!-- last verified: 2026-07-11 -->

# Telemetry Query Guide

How to actually pull data, via the MCP tool
`mcp__telemetry-query-bridge__query_telemetry`. Read
[telemetry-schema.md](telemetry-schema.md) for column names and value kinds before
building a filter.

## The tool

```
mcp__telemetry-query-bridge__query_telemetry
  dataset  (string, required)  "telemetry"
  date     (string, optional)  UTC day, "YYYY-MM-DD"; default = today (UTC)
  filter   (string, required)  a predicate over the telemetry columns
```

It returns the matching rows from exactly one day's partition
(`{storage}/telemetry/dt={date}/*.parquet`). It is **read-only and filter-only**:

- You cannot select specific columns, aggregate, sort, group, limit, or join.
- You filter rows; you reason over the returned rows yourself.
- Output is byte-bounded; a broad filter on a busy day may be truncated — prefer a
  selective filter and, if needed, narrow further and re-query.
- **Timeout is 30 s.** A filter without `event_type` scans every row for the day and
  will often time out. Always scope to one event type first, then add more conditions.

> **Lead with `event_type`.** Every row is either `PerCityCycle` or `FullCycle` —
> scoping on that first (e.g. `event_type = 'FullCycle' AND health_ok = false`) is
> almost always the right way to narrow a broad question before adding more
> conditions.

## Filter grammar (the allow-list)

Allowed, and nothing else:

- **Column names** from the telemetry schema (bare snake_case, no quotes, no dots).
- **Literals**: numbers (`-?\d+(\.\d+)?`), strings `'...'` (chars `[A-Za-z0-9 _-]`
  only), and bare booleans `true` / `false`.
- **Comparison operators**: `<`, `<=`, `>`, `>=`, `=`, `!=`.
- **Logical connectors**: `AND`, `OR` (case-insensitive).
- **Parentheses** for grouping.

Hard rules (a violation is rejected outright, not coerced):

1. The column must exist in the telemetry schema. Retired per-vehicle snap/lerp
   columns (`snap_distance_km`, `pos_delta_km`, etc.) are unknown columns now.
2. The literal kind must match the column kind:
   - numeric column ↔ number
   - string column ↔ `'quoted'`
   - timestamp column ↔ `'YYYY-MM-DD'` date string (date granularity; no `T`/`:`)
   - bool column ↔ bare `true`/`false`
3. No dotted identifiers (`a.b` → rejected). No `:` or `T` inside strings.
4. Forbidden anywhere: `;`, backtick, `$`, `|`, `&`, `\`, comment markers
   (`--`, `#`, `/* */`), SQL keywords (`SELECT`, `FROM`, `WHERE`, `UNION`, `JOIN`,
   `DROP`, `ATTACH`, `COPY`, `INSTALL`, `LOAD`, `SET`, `PRAGMA`, …), and any
   URL/path patterns (`azure://`, `http://`, `file:`, `..`).
5. Filter ≤ 256 characters.

> The `filter` is ONLY the predicate — what would come after `WHERE`. Do not write
> `WHERE`, `SELECT`, or a table name; those are forbidden keywords.

## Accept examples

| filter |
|--------|
| `event_type = 'PerCityCycle'` |
| `event_type = 'FullCycle'` |
| `health_ok = false` |
| `vehicles_processed > 0 AND health_ok = true` |
| `city_name = 'MARTA'` |
| `tones_emitted >= 5 OR feed_freshness_seconds > 60` |
| `observation_utc > '2026-07-11'` |
| `(event_type = 'FullCycle' OR event_type = 'PerCityCycle') AND gc_heap_bytes > 100000000` |
| `route_index_size > 0 AND crossing_baseline_cache_size >= 0` |

## Reject examples (and why)

| filter | Why it fails |
|--------|--------------|
| dataset `snap` / `lerp` / `cycle` | those datasets no longer exist — only `telemetry` |
| `snap_distance_km > 0.5` | retired per-vehicle column → unknown |
| `pos_delta_km > 1.0` | retired lerp column → unknown |
| `last_update_cache_size > 0` | dropped dead column → unknown |
| `health_ok = 1` | bool wants `true`/`false` |
| `health_ok = 'true'` | bool must be unquoted |
| `event_type = PerCityCycle` | string wants quotes |
| `tones_emitted = 'five'` | numeric wants a number |
| `observation_utc > '2026-07-11T00:00:00'` | `:`/`T` forbidden in strings |
| `event_type.value = 'x'` | dotted identifier — unexpected character |
| `SELECT * FROM telemetry` | forbidden keyword |

## Output format & parsing

The tool returns a **DuckDB ASCII box-drawing table**, not JSON. It looks like:

```
┌────────────┬────────────┬─ … ─┐
│ EVENT TYPE │ EVENT ID   │ …   │
├────────────┼────────────┼─ … ─┤
│ FullCycle  │ 680cc1b6…  │ …   │
   …
└────────────┴────────────┴─ … ─┘
2026-07-11 (N rows)
```

Consequences (all learned the hard way — don't rediscover them):

- **`jq` returns nothing and fails silently.** There is no JSON. Do not pipe results
  to `jq` or assume object structure.
- **Large results spill to a UTF-8 file.** When output exceeds the token limit the
  tool saves it to a `tool-results/…txt` file and gives you the path. Read that file —
  but **only with explicit UTF-8 decoding**. `Get-Content` and the Bash tool both
  mangle the box-drawing chars (`│ ┌ ├`) into Latin-1 garbage (`00E2 201A 00BD`…),
  which breaks every split. The pattern that works:

  ```powershell
  $lines = [System.IO.File]::ReadAllLines($path, [System.Text.Encoding]::UTF8)
  ```

- **Prefer PowerShell over the Bash tool for spilled files.** Bash `head` / `awk` /
  `cut` / `jq` returned empty output on these files; PowerShell with explicit UTF-8
  reads them reliably.
- **Parse by splitting on `│` (U+2502) and validate the cell count.** Map columns by
  matching the header cell text, then expect a fixed cell count per row and **skip any
  row that doesn't match** — the last data row and DuckDB's trailing `N rows` footer
  do not split cleanly.

  ```powershell
  $sep = [char]0x2502
  $header = $lines[1] -split $sep                  # line 0 is the top border
  # find a column's index by its header text, e.g. "CITIES PROCESSED CSV"
  $col = 0..($header.Count-1) | Where-Object { $header[$_].Trim() -eq "CITIES PROCESSED CSV" }
  foreach ($row in $lines) {
    $cells = $row -split $sep
    if ($cells.Count -ne $header.Count) { continue }   # border / footer / wrapped row
    $value = $cells[$col].Trim()
    # … reason over $value …
  }
  ```

  The `telemetry` table has **17 columns** (fewer populated per row depending on
  `event_type` — non-applicable columns come back empty, not absent).

### Recipe: count distinct values from a CSV column

Because the tool can't aggregate, the core move is **read rows, then reason in script**.
`cities_processed_csv` (FullCycle rows only) is a comma-separated list — to answer
"how many cities are being processed", split one cell on commas and count:

```powershell
$sep = [char]0x2502
$lines = [System.IO.File]::ReadAllLines($path, [System.Text.Encoding]::UTF8)
$header = $lines[1] -split $sep
$ci = 0..($header.Count-1) | Where-Object { $header[$_].Trim() -eq "CITIES PROCESSED CSV" }
foreach ($row in $lines) {
  $cells = $row -split $sep
  if ($cells.Count -ne $header.Count) { continue }
  $cities = $cells[$ci].Trim()
  $count  = if ($cities -eq "") { 0 } else { ($cities -split ",").Count }
  "{0}  cities={1}" -f $cells[1].Trim().Substring(0,8), $count
}
```

Comparing the count across the day's ticks also tells you whether it's **stable** or
churning, which is usually the real answer the user wants — not just a single
snapshot.

## Date handling

- Omit `date` to get today (UTC). Pass `date` explicitly when the user names a day.
- Format must be strict `YYYY-MM-DD`, zero-padded, a real calendar date
  (`2026-7-1` and `2026-13-40` are rejected).
- Each call reads exactly one day. To compare days, make one call per day and reason
  across the results.

## Working effectively

- **Start broad-but-cheap, then narrow.** A first probe like
  `event_type = 'FullCycle'` tells you the day has data and roughly how many ticks ran.
- **Empty result is a signal, not an error** — it often means "no rows matched",
  which can itself answer a yes/no question (e.g. "any unhealthy ticks today?").
- **To approximate a count/threshold** without aggregation: filter to the condition
  and count the rows that come back (mind output truncation on busy days — tighten the
  filter if you suspect truncation).
- **Pivot via `event_id`/`city_name`/time window yourself** — there's no cross-row
  join key like the old `cycle_id`; each row is independently identified. Correlate
  `PerCityCycle` and `FullCycle` rows by matching `observation_utc` within the same
  tick and `city_name` against `cities_processed_csv`.
- **On a rejection**, fix the filter against the rules above (most often: retired
  column, or wrong literal kind) — don't retry the same string.

## Error handling

The tool returns a plain error string on validation or execution failure (paths and
the storage connection string are stripped). Read the message, correct the offending
piece (dataset, date format, column, or literal kind), and re-query. Never invent a
result when a call fails — tell the user what failed and what you're adjusting.
