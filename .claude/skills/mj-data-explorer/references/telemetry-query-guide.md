<!-- last verified: 2026-06-11 -->

# Telemetry Query Guide

How to actually pull data, via the MCP tool
`mcp__telemetry-query-bridge__query_telemetry`. Read
[telemetry-schema.md](telemetry-schema.md) for column names and value kinds before
building a filter.

## The tool

```
mcp__telemetry-query-bridge__query_telemetry
  dataset  (string, required)  one of "snap" | "lerp" | "cycle"
  date     (string, optional)  UTC day, "YYYY-MM-DD"; default = today (UTC)
  filter   (string, required)  a predicate over THAT dataset's columns
```

It returns the matching rows from exactly one day's partition
(`{storage}/{dataset}/dt={date}/*.parquet`). It is **read-only and filter-only**:

- You cannot select specific columns, aggregate, sort, group, limit, or join.
- You filter rows; you reason over the returned rows yourself.
- Output is byte-bounded, so a broad filter on a busy day may be truncated — prefer
  a selective filter and, if needed, narrow further and re-query.

> **Anticipate truncation, especially on `cycle`.** A broad probe like `cycle` /
> `buses_processed > 0` can blow the token budget instantly — `cycle` rows carry the
> very wide `active_route_ids` / `active_vehicle_ids` CSV columns, so even a single
> day's ~30 cycles can exceed the limit (~57K chars). Assume `cycle` is large: lead
> with the tightest filter that answers the question, or expect the result to spill to
> a file and go straight to the file-read pattern below.

## Filter grammar (the allow-list)

Allowed, and nothing else:

- **Column names** from the chosen dataset (bare snake_case, no quotes, no dots).
- **Literals**: numbers (`-?\d+(\.\d+)?`), strings `'...'` (chars `[A-Za-z0-9 _-]`
  only), and bare booleans `true` / `false`.
- **Comparison operators**: `<`, `<=`, `>`, `>=`, `=`, `!=`.
- **Logical connectors**: `AND`, `OR` (case-insensitive).
- **Parentheses** for grouping.

Hard rules (a violation is rejected outright, not coerced):

1. The column must exist **in the selected dataset**. A column that's valid in another
   dataset is still rejected here.
2. The literal kind must match the column kind:
   - numeric column ↔ number
   - string column ↔ `'quoted'`
   - timestamp column ↔ `'YYYY-MM-DD'` date string (date granularity; no `T`/`:`)
   - bool column ↔ bare `true`/`false`
3. No dotted identifiers (`a.b` → unknown column). No `:` or `T` inside strings.
4. Forbidden anywhere: `;`, backtick, `$`, `|`, `&`, `\`, comment markers
   (`--`, `#`, `/* */`), SQL keywords (`SELECT`, `FROM`, `WHERE`, `UNION`, `JOIN`,
   `DROP`, `ATTACH`, `COPY`, `INSTALL`, `LOAD`, `SET`, `PRAGMA`, …), and any
   URL/path patterns (`azure://`, `http://`, `file:`, `..`).
5. Filter ≤ 256 characters.

> The `filter` is ONLY the predicate — what would come after `WHERE`. Do not write
> `WHERE`, `SELECT`, or a table name; those are forbidden keywords.

## Accept examples

| dataset | filter |
|---------|--------|
| `snap` | `snap_distance_km > 0.5` |
| `snap` | `is_stale = true` |
| `snap` | `observation_utc > '2026-06-04'` |
| `snap` | `(snap_outcome = 'Moved' OR snap_outcome = 'Stale') AND raw_lat > 33.0` |
| `lerp` | `pos_delta_km > 1.0 AND vehicle_id = 'v001'` |
| `cycle` | `buses_stale > 10 AND duplicate_feed = false` |
| `cycle` | `sidecar_dropped_records > 0` |

## Reject examples (and why)

| dataset | filter | Why it fails |
|---------|--------|--------------|
| `other` | anything | dataset not in `{snap,lerp,cycle}` |
| `snap` | `petal.length > 5` | unknown column / `.` not allowed |
| `snap` | `buses_stale > 10` | cycle column used on snap |
| `snap` | `is_stale = 1` | bool wants `true`/`false` |
| `snap` | `is_stale = 'true'` | bool must be unquoted |
| `snap` | `observation_utc > 1234567` | timestamp wants a date string |
| `snap` | `observation_utc > '2026-06-04T12:00:00'` | `:`/`T` forbidden in strings |
| `snap` | `raw_lat = 'abc'` | numeric column wants a number |
| `snap` | `vehicle_id = 123` | string column wants a quoted string |
| `cycle` | `SELECT * FROM cycle` | forbidden keyword |

## Output format & parsing

The tool returns a **DuckDB ASCII box-drawing table**, not JSON. It looks like:

```
┌──────────┬─────────────────┬─ … ─┐
│ CYCLE ID │ BUSES PROCESSED │ …   │
├──────────┼─────────────────┼─ … ─┤
│ 680cc1b6 │ 197             │ …   │
   …
└──────────┴─────────────────┴─ … ─┘
2026-06-11 (N rows)
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
  # find a column's index by its header text, e.g. "ACTIVE ROUTE IDS"
  $col = 0..($header.Count-1) | Where-Object { $header[$_].Trim() -eq "ACTIVE ROUTE IDS" }
  foreach ($row in $lines) {
    $cells = $row -split $sep
    if ($cells.Count -ne $header.Count) { continue }   # border / footer / wrapped row
    $value = $cells[$col].Trim()
    # … reason over $value …
  }
  ```

  The `cycle` table has **23 columns**; `snap` and `lerp` have their own fixed counts
  (derive once from the header, then reuse).

### Recipe: count distinct values from a CSV column

Because the tool can't aggregate, the core move is **read rows, then reason in script**.
The `cycle` columns `active_route_ids` / `active_vehicle_ids` are comma-separated sorted
distinct lists — to answer "how many routes/buses are being processed", split one cell
on commas and count:

```powershell
$sep = [char]0x2502
$lines = [System.IO.File]::ReadAllLines($path, [System.Text.Encoding]::UTF8)
$header = $lines[1] -split $sep
$ri = 0..($header.Count-1) | Where-Object { $header[$_].Trim() -eq "ACTIVE ROUTE IDS" }
foreach ($row in $lines) {
  $cells = $row -split $sep
  if ($cells.Count -ne $header.Count) { continue }
  $routes = $cells[$ri].Trim()
  $count  = if ($routes -eq "") { 0 } else { ($routes -split ",").Count }
  "{0}  routes={1}" -f $cells[1].Trim().Substring(0,8), $count
}
```

Comparing the per-cycle count across the day's cycles also tells you whether the number
is **stable** or churning (routes flicker in/out as vehicles report), which is usually
the real answer the user wants — not just a single snapshot.

## Date handling

- Omit `date` to get today (UTC). Pass `date` explicitly when the user names a day.
- Format must be strict `YYYY-MM-DD`, zero-padded, a real calendar date
  (`2026-6-4` and `2026-13-40` are rejected).
- Each call reads exactly one day. To compare days, make one call per day and reason
  across the results.

## Working effectively

- **Start broad-but-cheap, then narrow.** A first probe like `cycle` /
  `buses_processed > 0` tells you the day has data and roughly how many cycles ran.
- **Empty result is a signal, not an error** — it often means "no rows matched",
  which can itself answer a yes/no question (e.g. "any persist failures today?").
- **To approximate a count/threshold** without aggregation: filter to the condition
  and count the rows that come back (mind output truncation on busy days — tighten the
  filter if you suspect truncation).
- **Pivot via join keys yourself.** `cycle_id` links `snap`/`lerp` rows to a `cycle`
  row; `vehicle_id` links a bus across `snap` and `lerp`. Query each dataset
  separately and correlate in your reasoning.
- **On a rejection**, fix the filter against the rules above (most often: wrong
  dataset for the column, or wrong literal kind) — don't retry the same string.

## Error handling

The tool returns a plain error string on validation or execution failure (paths and
the storage connection string are stripped). Read the message, correct the offending
piece (dataset, date format, column, or literal kind), and re-query. Never invent a
result when a call fails — tell the user what failed and what you're adjusting.
