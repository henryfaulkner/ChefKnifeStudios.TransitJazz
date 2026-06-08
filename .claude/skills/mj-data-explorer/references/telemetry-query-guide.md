<!-- last verified: 2026-06-07 -->

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
