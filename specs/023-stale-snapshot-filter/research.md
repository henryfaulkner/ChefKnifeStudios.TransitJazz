# Research: Stale Snapshot Filter

All decisions below were resolved in a grill-me design session prior to planning. No `NEEDS CLARIFICATION` markers remain. Each entry records the decision, rationale, and the alternatives rejected.

## R1. Where filtering happens — serve-time vs. cache-write-time

**Decision**: Filter/merge at **cache-write time** (inside `LastBatchCache.Set`), maintaining a per-vehicle accumulator.

**Rationale**: Serve-time filtering can only operate on the single most-recent batch the cache holds. When that batch is entirely stale (the exact observed symptom: 186/186 stale), serve-time filtering returns an **empty** snapshot — worse than today. Only a write-time accumulator can retain the last meaningful reading per vehicle across batches and thus satisfy the DoD ("non-empty, useful picture on cold load even when the latest batch is all-stale").

**Alternatives considered**:
- *Serve-time projection in `GetLastBatch`*: simplest, but structurally cannot meet the all-stale DoD; rejected.
- *Filter at write but store only the latest filtered batch (no cross-batch retention)*: a vehicle whose latest record is stale vanishes; rejected for the same DoD reason.

## R2. Dictionary key and stored value

**Decision**: Key on `VehicleId` (string) alone. Store the **whole** `RouteNearestPointRecord` verbatim.

**Rationale**: `VehicleId` is "the bus." Keying on `(VehicleId, RouteId)` could briefly render one physical bus as two markers across a route change. Storing the full record preserves every field the animator consumes (position, speed, bearing, timestamps) and honors the constraint not to change the record shape.

**Alternatives considered**:
- *Key on `(VehicleId, RouteId)`*: risks duplicate markers per bus during re-snap; rejected.
- *Store a projected subset*: risks dropping a field the client needs and adds a reshape step; rejected.

## R3. Snapshot assembly shape

**Decision**: Assemble a **single** `EventEnvelope` wrapping a single `RouteNearestPointBatchEvent` whose `BatchRecords` = the dictionary values. `EventType = nameof(RouteNearestPointBatchEvent)`; `Timestamp = DateTimeOffset.UtcNow` at assembly. Build the snapshot **inside `Set`** (once per write) and cache the reference; `Current` returns it.

**Rationale**: This is structurally identical to what the Worker already emits (`Worker.cs` builds exactly one such envelope per cycle), so the client cannot distinguish a snapshot from a live frame except that it carries no stale records. Building in `Set` keeps `Current` a cheap lock-free read (writes ~every 10s, reads rarer). The envelope `Timestamp` is not consumed for motion (records carry their own `CurrentUtcNow`/`PriorUtcNow`), so serve-time-now is honest and harmless.

**Alternatives considered**:
- *Preserve original per-envelope grouping*: meaningless to the client (it flattens all batches through one animator path) and adds tracking cost; rejected.
- *Build snapshot lazily in `Current`*: allocates on every read; rejected in favor of build-on-write.
- *Use max record timestamp for the envelope*: marginally more "correct" but scans for a value nobody reads; rejected.

## R4. Merge rule

**Decision**: Per record extracted from an incoming batch, keyed by `VehicleId`:
- **Non-stale** → upsert (replace the stored record; latest meaningful wins).
- **Stale, vehicle already retained** → ignore (keep the existing meaningful record; do not overwrite).
- **Stale, vehicle not yet retained** → drop (cannot seed a position that was never observed meaningfully).

No eviction, no TTL. A fully-stale or empty batch leaves the dictionary unchanged → the prior snapshot is preserved.

**Rationale**: Stale means "same GPS reading, no new motion," so the retained non-stale record remains the best-known position. Preserving the dictionary on an all-stale batch is precisely what makes the cold-load case work. Fleet size is bounded, and a worker restart bounds growth, so omitting TTL avoids wall-clock reasoning and per-entry timestamp bookkeeping for no cold-start benefit.

**Alternatives considered**:
- *Let stale refresh position*: contradicts the meaning of stale; rejected.
- *Add TTL / staleness expiry*: real complexity (timestamps, wall-clock, test surface) for a problem a restart already bounds; deferred to a possible future feature.

## R5. Thread-safety

**Decision**: A single `lock` around the read-modify-write (merge + rebuild) in `Set`. `Current` returns `Volatile.Read(ref _current)` of the prebuilt immutable snapshot.

**Rationale**: The new `Set` reads prior state (the dictionary) before writing, so the old lock-free single-pointer-swap pattern is unsafe if two `PublishBatch` calls ever overlap (hub method; reconnect storms). A `lock` makes "merge then publish snapshot" atomic as a unit. Readers never touch the dictionary — they only read a fully-built published snapshot reference — so reads stay cheap and never tear. A `ConcurrentDictionary` is insufficient because it makes individual ops atomic but not the compound merge-then-rebuild.

**Alternatives considered**:
- *Keep `Volatile`-only*: unsafe for read-modify-write; rejected.
- *`ConcurrentDictionary`*: doesn't make the compound rebuild atomic; rejected.

## R6. Payload extraction and non-matching payloads

**Decision**: In `Set`, iterate envelopes; for each, pattern-match `Payload is RouteNearestPointBatchEvent rnp` and merge its `BatchRecords`. Skip any envelope whose payload is not a `RouteNearestPointBatchEvent`.

**Rationale**: Verified against the codebase: `RouteNearestPointBatchEvent` is the **only** `ISignalREvent` implementer, and `Worker.cs` publishes exactly one such envelope per cycle — so the skip branch is defensive-only and never fires today. Using the typed pattern-match (not the `EventType` string) is safer on the server (no typo/discriminator reliance) and yields the typed reference for free. The outgoing assembled envelope still **writes** `EventType = nameof(RouteNearestPointBatchEvent)` so the client's JSON discriminator deserializes correctly.

**Alternatives considered**:
- *Match on the `EventType` string*: relies on the discriminator being set correctly; the server holds the real typed object, so the type check is strictly better; rejected.
- *Pass non-matching payloads through into the snapshot*: a non-vehicle event has no place in a per-vehicle merge; if a second event type is ever added it should deliberately revisit the cache; rejected.

## R7. Empty cases

**Decision**: When the merged dictionary is empty, `Current` is `Array.Empty<EventEnvelope>()` (never a one-element list carrying an empty `BatchRecords`). Before any `Set`, `Current` is empty (unchanged from today). An empty/all-stale incoming batch leaves the prior snapshot intact.

**Rationale**: DoD forbids empty envelopes. Under the merge rule a non-empty dictionary always yields ≥1 record, so the only empty path is an empty dictionary → empty collection. Preserving the prior snapshot on a no-op batch is the cold-load guarantee.

## R8. Existing client-side workaround

**Decision**: Leave the `vehicle-animator.js` idle-seed in place; no client revert bundled here.

**Rationale**: The live SignalR stream still carries stale records (clients with existing state need them to re-anchor), so the idle-seed retains a narrow live-path job (a client whose first live frame for a vehicle is stale, with no prior state). It is now defensive rather than redundant. This feature is server-side only; touching the client would violate scope and couple a clean server change to a client change with its own verify burden.

## Codebase verification performed

- `ILastBatchCache` / `LastBatchCache`: single-slot `Volatile` holder — confirmed.
- `WorkerTransitHub.PublishBatch`: `Set(batch)` then `Clients.All.SendAsync("ReceiveBatch", batch)` — relay sends the raw batch directly; safe to leave untouched.
- `TransitEndpoints.GetLastBatch`: returns `cache.Current` — no change needed.
- `Program.cs:72`: `AddSingleton<ILastBatchCache, LastBatchCache>()` — singleton confirmed; per-vehicle accumulation across requests is valid.
- `ISignalREvent` implementers: only `RouteNearestPointBatchEvent`.
- `Worker.cs:428`: emits one `EventEnvelope(nameof(RouteNearestPointBatchEvent), UtcNow, new RouteNearestPointBatchEvent(batch))` per cycle.
