# Contract: HTTP Compression & Route-Response Caching (Phase 1)

Binds spec FR-004, FR-005, FR-006 / US2. Response *content* (the JSON the client deserializes) is byte-for-byte unchanged — only transfer encoding, headers, and server-side computation change.

## C1. Response compression (server-wide)

- Registration in `Server.WebAPI/Program.cs`: `AddResponseCompression` with `EnableForHttps = true`, providers Brotli then Gzip (order = preference), default MIME types (includes `application/json`). `UseResponseCompression()` placed before endpoint mapping.
- Applies to all HTTP endpoints; does NOT apply to WebSocket/SignalR traffic (out of middleware scope by design).

| Request | Response |
|---|---|
| `GET /gtfs/route-shapes?city=marta` with `Accept-Encoding: br` | 200, `Content-Encoding: br`, body ≤30% of identity size |
| Same with `Accept-Encoding: gzip` | 200, `Content-Encoding: gzip` |
| Same with no `Accept-Encoding` | 200, identity body, still correct |
| SignalR negotiate/websocket | unchanged, uncompressed by this middleware |

## C2. Precomputed cached responses + conditional GET

Endpoints in scope: `GetAllRouteShapes` (per-city and no-city variants) and `GetAllRoutes` (per-city). Out of scope: `GetRouteShape`, `GetSubwayStopOffsets`, debug keys.

**Server obligations**:
1. Response bytes come from `IRouteShapeResponseCache` — no per-request `JsonSerializer.Deserialize`/re-serialize of stored blobs on the 200 path.
2. Cache entries (re)built exactly when `GtfsStaticLoader` completes a city's load (startup + 24h refresh); atomic reference swap; single writer.
3. 200 responses carry `ETag: "<strong-hash-of-bytes>"` and `Cache-Control: public, max-age=3600`.
4. `If-None-Match` containing the current ETag → `304 Not Modified`, no body, ETag echoed.
5. GTFS static not ready → `503` (existing semantics preserved).
6. After a daily refresh changes the data, the ETag changes; a revalidating client receives 200 + full new body.

| Scenario | Expectation |
|---|---|
| Cold visit | 200 + ETag + Cache-Control, compressed per C1 |
| Reload within 1h, same data | Browser serves from HTTP cache (no request) or 304 on revalidate |
| Revalidate after refresh with changed shapes | 200, new ETag, new body |
| Two concurrent requests during a cache swap | Each gets a self-consistent entry (old or new), never a torn body |
| Unknown city param | Same status/shape as today (empty list 200), not 500 |

**Client obligations**: none — browser HTTP caching does the work; `GtfsEndpointsService` keeps deserializing a 200 exactly as today. (HttpClient in WASM delegates to fetch, which handles 304/cache transparently.)

## Invariants
- Deserialized `RouteShapeFeature` content identical pre/post feature for identical stored data (regression-tested by comparing old-path vs cached-path JSON).
- No new endpoint routes, no route renames, no auth changes.
