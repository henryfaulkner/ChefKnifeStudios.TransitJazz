# Contract: `generate.py` CLI

The tool's external interface is its command line, its stdout summary, and its exit code.

## Invocation

```
python generate.py [--geojson PATH] [--api BASE_URL] [--out-dir DIR]
```

| Argument | Default | Description |
|----------|---------|-------------|
| `--geojson` | `~/Downloads/Official_Neighborhoods_with_Current_Demographic_Data_(2024).geojson` | Path to source neighborhood GeoJSON. |
| `--api` | `https://marta-jazz-dev-ca-server.jollytree-dd5ca774.eastus2.azurecontainerapps.io` | MJ API base URL (no trailing slash; tool appends `/gtfs/routes/shapes`). |
| `--out-dir` | `.` (script's own directory) | Directory to write the two JSON outputs into. |

The script header MUST carry a comment stating it is run manually and the re-generation triggers (GTFS shape changes; GeoJSON source update) — design doc §8.

## Outputs (on success)

Writes both files into `--out-dir`:
- `neighborhood_routes.json` (lean — see `lean-output.schema.md`)
- `neighborhood_routes_full.json` (full — see `full-output.schema.md`)

Both files are written **only after the join completes successfully** — never partially (FR-014).

## stdout summary (on success) — FR-013

Human-readable summary including, at minimum:
- `Total neighborhoods processed: <N>`
- `Neighborhoods with >=1 route: <N>`
- `Neighborhoods with 0 routes (<N>): <comma-separated names>`
- `Total unique routes matched: <N>`

(Recommended) skipped-feature counts for malformed neighborhood/route geometries.

## Exit codes

| Code | Condition |
|------|-----------|
| `0` | Both files written successfully; summary printed. |
| non-zero | Any fatal error: GeoJSON unreadable/unparseable; API unreachable / non-2xx / unparseable / empty; output dir unwritable. A clear message MUST go to stderr. **No output files written** on a fatal API error (FR-014). |

## Accept / reject behavior

| Scenario | Expected |
|----------|----------|
| Valid GeoJSON + reachable API | exit 0, both files written, summary printed |
| API returns 5xx / times out / connection refused | exit non-zero, stderr error, **no files written** |
| API returns empty array | exit non-zero (treated as failure — no routes would be misleading), no files written |
| GeoJSON path missing/invalid | exit non-zero, stderr error, no files written |
| Neighborhood with no matching route | included in lean output with `"routes": []`; name listed in stdout 0-route list |
| Neighborhood missing a demographic field | that field is `null` in lean output (never `0`) |
| MultiPolygon neighborhood | handled identically to Polygon (shapely `shape()`); no special case |
