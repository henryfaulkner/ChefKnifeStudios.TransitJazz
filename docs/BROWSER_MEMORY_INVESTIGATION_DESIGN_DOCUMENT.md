# Browser Memory Investigation — Design Document

> **Purpose of this document.** This is a self-contained record of *why the TransitJazz
> Blazor WebAssembly client consumes a large and growing amount of browser RAM*, and a
> menu of remediation options for a future work item. It is an **investigation + design**
> doc, not an implementation. No code has been changed. Every claim below is anchored to a
> specific file and line so a future agent (or human) can verify it independently before
> acting. Read this first; do not assume a leak is fixed because it is described here.

**Status:** **ATTRIBUTED via heap snapshot (2026-06-21).** See §-1 (Conclusion) first — it
supersedes the earlier suspect hierarchy. The leading on-paper suspects (MapLibre cache,
route geometry) were **measured and ruled out**; the JS heap is dominated by the .NET WASM
linear memory.
**Date:** 2026-06-20 (investigation) · 2026-06-21 (heap-snapshot attribution)
**Component:** `src/Client/` (Blazor WebAssembly, MapLibre GL, Tone.js)
**Branch observed on:** `bug/fix-mobile-audio`

---

## -1. CONCLUSION (heap-snapshot evidence — supersedes §0/§1 suspect ranking)

A real Chrome heap snapshot (`Heap-20260621T182850.heapsnapshot`, 49 MB) was analyzed with
`tools/analyze-heapsnapshot.mjs` + `tools/trace-up.mjs`. Findings:

- **JS-heap total self_size ≈ 170 MB.** Of that, **88.9% (151 MB) is `native` nodes**, and
  the single largest object is **one 79.81 MB `JSArrayBufferData`**.
- **That 79.81 MB buffer IS the .NET WASM linear memory.** Retainer chain (verified):
  `JSArrayBufferData ← ArrayBuffer (backing_store) ← WebAssembly.Memory + HEAP8/HEAPU8 views
  (context:HEAP8) ← WasmTrustedInstanceData ← getStreamFromFD (emscripten)`. This is the
  whole .NET heap, which a JS snapshot represents as a single ArrayBuffer.
- **The on-paper prime suspects were measured and are NEGLIGIBLE:**
  - Route geometry (`_routesFeatureCollection`, `routeGeometry`, coordinates): **< 0.2 MB**.
  - MapLibre WebGL: `WebGLBuffer` **0.11 MB**, `WebGLTexture` **0.01 MB**, all tile/shader
    data combined **< 1 MB**. **§0.2 and §3.7 are refuted.**
  - Vehicle/animator/Tone objects: sub-MB. **§3.1–§3.5 are refuted as the cause.**

**Therefore: the footprint is the .NET WebAssembly heap itself, not app data, not MapLibre,
not the route cache, not a JS leak.**

### Caveat — the snapshot is 170 MB but the symptom is ~1.2 GB

A Chrome heap snapshot captures the **JS heap only**. It does **not** include: GPU/VRAM
behind WebGL textures, decoded-image memory, audio device buffers, or — crucially — the
**WASM heap's reserved-but-snapshot-collapsed pages and the browser/renderer process
overhead**. The snapshot was almost certainly taken after a forced GC (DevTools does this),
so it shows the *live* JS+WASM working set (~170 MB), **not** the resident process RSS the
OS/Task-Manager reports (~1.2 GB). The ~1.2 GB is dominated by:

1. **WASM heap high-water + reservation.** Mono/WASM grows linear memory in large chunks and
   **does not return pages to the OS**; the RSS reflects the peak, while the snapshot reflects
   post-GC live bytes. This is the prime driver and is consistent with "flat 1.2 GB, prod ==
   dev."
2. **Renderer/GPU process overhead** (Chrome multiprocess, WebGL context, MapTiler tiles in
   GPU memory) which a JS heap snapshot never shows.

### What this redirects the work toward

Stop hunting JS-side leaks and MapLibre/route-cache size — measurements say they're not it.
The lever is the **.NET WASM heap's size and its peak**:

1. **Confirm RSS vs. live with `performance.measureUserAgentSpecificMemory()`** (now exposed
   via `MemoryProbe.measureUA()`) — get the WASM/`Wasm` and `GPU`/canvas lines to split the
   1.2 GB into WASM-heap vs. renderer/GPU. This is the one remaining unknown.
2. **Reduce WASM peak working set:** the demoted-but-real allocation churn (§3.3) and the
   per-frame interop projections drive transient .NET allocations that **raise the
   never-returned high-water mark**. Cutting churn (Option B) now matters again — not as a
   "leak" fix but to lower the peak the WASM heap reserves and holds.
3. **Build/runtime trims of the WASM heap itself:** `<EmccMaximumHeapSize>` caps reservation;
   AOT/trimming change steady-state; investigate whether something is doing a large one-time
   allocation early (e.g. deserializing the full route set / initial snapshot) that sets a
   high peak the heap then never gives back.
4. **Verify GPU share** separately (it's invisible to the snapshot): a full MapTiler vector
   basemap can hold hundreds of MB of *GPU* tiles even though its JS-side `WebGLBuffer`s are
   tiny — so the "empty LightOff style" test (Option F) is still worth running, but judged by
   RSS/GPU memory, not by the JS heap snapshot.

> Reproduce: `node --max-old-space-size=4096 tools/analyze-heapsnapshot.mjs <snapshot>` and
> `tools/trace-up.mjs <snapshot>`. Scripts live in `tools/`.

---

## 0. OBSERVED SYMPTOM (read this first — it overrides §1)

**Measured behavior: resident usage sits at ~1.2 GB, flat over time, in BOTH the local dev
build AND the deployed production environment.**

### 0a. What "prod is also ~1.2 GB" eliminates

Production is a `Release`, optimized/trimmed publish. It being **equal** to the local number
**rules out §0.1 (Debug/untrimmed build)** as the cause — do *not* spend time on
`PublishTrimmed`/AOT to fix *this* number (still fine as general hygiene, just not the lever
here). The footprint is therefore **environment-independent and data/runtime-driven**, which
leaves exactly two families of cause:

1. **MapLibre GL's WebGL/JS runtime heap (§0.2)** — vector tiles, glyph atlases, sprite
   sheets, tessellated buffers for the MapTiler basemap. Prod loads a **full MapTiler vector
   style even for "LightOff"** (`appsettings.json:30` points LightOff at a real style.json,
   not a blank canvas), so MapLibre caches real basemap data regardless of the toggle.
2. **Route geometry held 3–4× (§3.7)** plus the per-frame GeoJSON the animator hands MapLibre.

A secondary aggravator confirmed in prod config: **`Logging.LogLevel.Default` is `Debug` in
production** (`appsettings.json:21`). Combined with the heavy per-batch/per-frame
`console.*` and `Logger.LogDebug` calls (animator `_log`, `transit-synth`, `map-interop`,
`HandleVehicleBatchAsync`), the browser console retains live references to logged objects.
This is real waste in prod but is unlikely to be the *bulk* of 1.2 GB on its own.

### 0b. Code reading has reached its limit — the next step is a measurement only you can take

Which of the two families owns the 1.2 GB **cannot be determined by reading source.** It
requires a DevTools snapshot on the **running** app (prod or dev — they're equal):

- **DevTools → Memory → Heap snapshot** = the **JS** heap (MapLibre + route GeoJSON +
  animator). Look at total size and the retained size of MapLibre internals vs.
  `ChefMap._routesFeatureCollection` / `ChefMapAnimator.routeGeometry`.
- **WASM linear memory** is reported separately (e.g. via `performance.measureUserAgentSpecificMemory()`
  in a cross-origin-isolated context, or the Memory panel's "JS VM instance" vs. WASM split).
- **`performance.measureUserAgentSpecificMemory()`** is the cleanest single call: it breaks
  down bytes by type (JS, DOM, **WebGL/canvas**, WASM, shared). **Run this in the prod
  console** — the WebGL line will immediately confirm or refute MapLibre as the culprit.

Until that breakdown exists, anything below is ranked probability, not proof.

---

**Original symptom note (retained):** the number is ~1.2 GB and does *not* grow over time.

This is the single most important fact and it **changes the conclusion**. A *flat* high
number means the problem is **steady-state size, not an unbounded leak**. Specifically:

- The "vehicles never evicted" / "tracker state never pruned" findings below (§3.1, §3.2)
  would produce a number that **climbs** over a session. Since the number is *flat*, those
  are **demoted** — they are real code smells and worth fixing for robustness, but they are
  **not** what is consuming the 1.2 GB. Do not lead with them.
- ~1.2 GB is also **far above** the 150–300 MB baseline estimated in §3.6. So the cause is
  something that allocates **large, once, and holds it flat** — most likely a combination of:
  1. **A Debug / unoptimized / untrimmed WASM build.** The shipped `index.html` and the
     `.csproj` carry **no** `<RunAOTCompilation>`, `<PublishTrimmed>`, `<WasmEnableSIMD>`,
     or `<EmccMaximumHeapSize>` settings, and the running copy is under `bin/Debug/`. A
     Debug Blazor-WASM heap is dramatically larger than a `Release`/trimmed publish — this
     alone can account for hundreds of MB to >1 GB of flat footprint.
  2. **MapLibre GL's WebGL tile + buffer cache** for the basemap (raster/vector tiles,
     glyph atlases, sprite sheets) — a large, flat GPU/JS allocation that scales with
     viewport, zoom range (`minZoom: 7 … maxZoom: 18`, `map-interop.js:22-23`), and
     `devicePixelRatio`. This is the classic source of a flat ~hundreds-of-MB-to-GB number
     on map apps and is independent of the .NET heap.
  3. **The full MARTA route geometry held in triplicate** (see §3.7 added below).

**Before acting, the first question to answer is not "where is the leak" but "is this a
Debug build, and how much of the 1.2 GB is the WASM heap vs. the JS/WebGL heap?"** See the
revised confirmation steps in §6.

---

## 1. TL;DR *(superseded by §0 — retained for the leak-hunting analysis)*

The client's browser memory footprint = **a large fixed baseline** (the .NET WASM runtime
+ MapLibre + Tone.js, structural and mostly unavoidable) **plus several genuinely
unbounded leaks** that grow for as long as the tab stays open. The dominant leak is that
**animated vehicles are never evicted** — `ChefMapAnimator.vehicles` accumulates an entry
for every vehicle ID MARTA has *ever* reported, and the 60 fps render loop iterates and
re-serializes that ever-growing set on every frame. Fixing vehicle eviction is the single
highest-value change. Secondary contributors are per-frame allocation churn (GC pressure)
and a pending-batch buffer that is unbounded if the map never becomes ready.

---

## 2. Architecture context (why two heaps)

This is **Blazor WebAssembly**, so two independent heaps compete for the browser's memory
budget:

1. **The .NET WASM heap** — your C#. The entire .NET runtime and every project assembly
   are downloaded and resident in linear memory. The Mono/WASM GC is conservative and slow
   to return freed pages to the OS, so the heap's high-water mark tends to *stick*.
2. **The JS heap** — MapLibre GL (WebGL tiles, vector geometry, GeoJSON sources), Tone.js
   (decoded audio buffers + Web Audio graph), and the hand-written animator / tracker /
   pulse modules under
   `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/wwwroot/js/`.

A leak in *either* heap shows up as "the browser tab is using a lot of RAM." Both heaps
have problems here.

---

## 3. Findings

Ordered by impact. Severity = (size of growth) × (how continuously it grows).

### 3.1 — CRITICAL: Vehicles are never evicted (`vehicle-animator.js`)

**File:** `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/wwwroot/js/vehicle-animator.js`

`ChefMapAnimator.vehicles` (line 2) and `ChefMapAnimator.routeGeometry` (line 3) are plain
objects used as maps keyed by vehicle ID / route ID. Throughout
`processNearestPointBatch` (lines 258–470) entries are **created and updated** —
`this.vehicles[rec.vehicleId] = { ... }` at lines 322 and 442 — but **no code path ever
deletes a key**. There is no TTL, no "out of service" sweep, no cap.

Consequences:

- Over hours, every transient vehicle ID the GTFS-RT feed has ever emitted stays resident,
  each holding a `history` ring buffer, `subPath`, `subPathCumDist`, `currentPos`, etc.
- The render loop `tick(now)` (lines 173–254) runs on `requestAnimationFrame` (~60 fps).
  Every frame it iterates **all** vehicle IDs (line 182), builds a brand-new `features`
  array (line 187, pushed at 227), and calls `this._source.setData({...})` once per frame
  (line 241). As the vehicle set grows without bound, per-frame CPU **and** the size of the
  GeoJSON handed to MapLibre grow with it. Idle/stale vehicles are still included as
  features every frame (the `idle` branch at line 195 still falls through to the
  `features.push` at 227).
- `routeGeometry` is bounded by route count (small), so it is not the leak — but dead
  vehicles referencing it keep the broader graph alive.

**This is the primary driver of unbounded growth.**

### 3.2 — CRITICAL (paired): Checkpoint tracker vehicle state never pruned (`checkpoint-tracker.js`)

**File:** `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/wwwroot/js/checkpoint-tracker.js`

`_vehicleState` (line 5, `Map<vehicleId, {...}>`) gains an entry on first observation of
each vehicle (`_vehicleState.set(...)` at line 99) and is **only ever cleared wholesale**
in `clear()` (line 36), which runs on component dispose — i.e. never during a normal
session. The per-tick hook (`_installTickHook`, lines 155–181) walks
`window.ChefMapAnimator.vehicles` every frame, so it inherits the same unbounded set as
3.1. Any eviction fix must prune **both** `ChefMapAnimator.vehicles` and this
`_vehicleState` so they don't drift out of sync.

### 3.3 — MODERATE: Per-frame allocation churn → WASM GC pressure

Two hot paths allocate continuously:

- **JS side:** `tick()` allocates a fresh `features` array and a new
  `{ type: 'FeatureCollection', features }` object **every animation frame**
  (`vehicle-animator.js:187,241`), ~60×/second.
- **.NET side:** `HandleVehicleBatchAsync`
  (`src/Client/ChefKnifeStudios.MartaJazz.Client.WebApp/Pages/TransitMap.razor.cs:395–433`)
  runs `.Where().Select().SelectMany().ToArray()` (lines 404–408), then a second
  `.Where(...).ToArray()` (line 410), then projects ~186 records into anonymous objects
  (lines 414–426) which are JSON-serialized across the JS interop boundary on **every ~10 s
  SignalR batch**. Plan note (`specs/023-stale-snapshot-filter/plan.md:20`) records ~186
  records per batch.

This does not "leak" in the strict sense, but the conservative WASM GC inflates and holds a
high water mark under sustained churn, so it directly worsens the reported number and makes
it spiky.

### 3.4 — MODERATE (conditional): `_pendingBatches` unbounded if the map never readies

**File:** `src/Client/ChefKnifeStudios.MartaJazz.Client.WebApp/Pages/TransitMap.razor.cs:56`

`_pendingBatches` accumulates any batch that arrives before the map signals ready
(`HandleVehicleBatchAsync` → `_pendingBatches.Add(batch)` at line 399). It is drained
exactly once in `OnMapReadyAsync` (lines 303–310). In the happy path this is harmless. But
if the map's `notifyMapReadyAsync` never fires (bad style URL, WebGL context failure,
mobile background-tab throttling), **every ~10 s SignalR batch is retained forever** on the
.NET heap, each holding a full `List<EventEnvelope>`. This is a plausible contributor on
flaky mobile sessions specifically — relevant given the current branch is
`bug/fix-mobile-audio`.

### 3.5 — MINOR: Tone.js sampler cache only grows (`transit-synth.js`)

**File:** `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/wwwroot/js/transit-synth.js`

`_instrumentCache` (line 7) holds one `T.Sampler` per route (set at line 87), each owning
decoded MP3 audio buffers fetched from the soundfont CDN (lines 58–65). It is bounded by
**route count** (small and finite), so it is not a runaway leak. However `dispose()`
(lines 183–190) — which would free the samplers and the Web Audio graph — is **never called
during a normal session**, only available as an exported API. Low priority; listed for
completeness.

### 3.7 — STEADY-STATE: Full route geometry held in triplicate (likely a real chunk of the 1.2 GB)

The complete set of allowed MARTA route shapes is resident **simultaneously in three
places**, none of which is freed during a session:

1. **.NET heap:** `_routeShapeCache` (`TransitMap.razor.cs:64`), a
   `Dictionary<string, RouteShapeFeature>` holding every route's full coordinate array for
   the page lifetime (comment at line 63 says exactly this). Populated in `LoadRoutesAsync`
   (lines 500–518).
2. **JS heap (animator):** `ChefMapAnimator.routeGeometry` (`vehicle-animator.js:3`),
   populated by `loadRouteGeometry` (lines 149–153) which stores both the raw `coords`
   **and** a computed cumulative-distance array `cumDist` (line 151) — roughly doubling the
   per-route coordinate memory.
3. **JS heap (MapLibre):** the `routes` GeoJSON source + the cached
   `ChefMap._routesFeatureCollection` (`map-interop.js:473`, added as a source at
   `addAllRoutes` lines 475–491), plus MapLibre's internal tessellated WebGL line buffers
   derived from it.

For a full MARTA system this is a large, flat allocation duplicated 3× (4× counting
`cumDist`). It does not grow, which is consistent with the observed flat 1.2 GB. This is a
**prime suspect** for the steady-state size and is much more likely to matter than the
demoted leak findings.

### 3.6 — STRUCTURAL: Fixed WASM + library baseline (not a leak)

Blazor WASM ships the .NET runtime and all assemblies into linear memory; MapLibre GL keeps
WebGL buffers + vector tiles resident; Tone.js holds decoded audio. Expect a **~150–300 MB
resident baseline before any vehicle data flows**, independent of the leaks above. This is
why the absolute number looks alarming next to a plain-JS map app. It can be *reduced*
(trimming, AOT settings, lazy-loading assemblies) but not eliminated. Out of scope for a
leak fix; noted so the baseline isn't mistaken for a leak.

---

## 4. Suspect summary table

Re-prioritized for the **observed flat ~1.2 GB** symptom (§0). "Severity" now reflects
*likely contribution to the flat 1.2 GB*, not leak risk.

| # | Location | Kind | Grows? | Likely share of 1.2 GB |
|---|----------|------|--------|------------------------|
| §0.2 | MapLibre WebGL tile/glyph/sprite cache (full vector style even when "off") | Steady-state | No | **Prime suspect** |
| 3.7 | Route geometry held 3–4× (.NET + animator + MapLibre) + per-frame GeoJSON | Steady-state | No | **High** |
| 0a | `LogLevel.Debug` in prod + heavy hot-path logging (console retains refs) | Steady-state | Slowly | Moderate aggravator |
| 3.6 | WASM runtime + MapLibre + Tone.js baseline | Steady-state | No | Moderate (structural) |
| 3.3 | `tick()` + `HandleVehicleBatchAsync` allocation churn | Churn | No (flat high-water) | Low–Moderate |
| 3.1 / 3.2 | `vehicles` / `_vehicleState` never evicted | Leak | Yes — but symptom is flat | Demoted (robustness only) |
| 3.4 | `_pendingBatches` | Conditional leak | Only if map never readies | Low (unless mobile-stuck) |
| 3.5 | `_instrumentCache` | Bounded growth | No | Minor |
| ~~§0.1~~ | ~~Debug/untrimmed WASM build~~ | — | — | **RULED OUT** — prod (Release) is equal |

---

## 5. Remediation options (for a future work item)

These are **design options, not decisions.** Since prod (Release) == dev == 1.2 GB, the
build is **not** the lever (Option E removed). **Step 0 is the measurement in §0b** — take
the `performance.measureUserAgentSpecificMemory()` breakdown before implementing anything, so
you target the heap that actually owns the bytes. Then pursue **Option F** (steady-state
route/MapLibre footprint), which the evidence most strongly implicates.

### Option F — Shrink the steady-state route/MapLibre footprint (addresses 3.7 + §0.2)

- **De-duplicate route geometry (3.7):** the .NET `_routeShapeCache` is kept only to
  re-render routes after a basemap style swap (`RenderRoutesAsync`). Evaluate whether it can
  be dropped (or stored more compactly) once MapLibre + the animator hold the data, since
  MapLibre already retains `_routesFeatureCollection` for the same purpose.
- **Tame MapLibre's cache (§0.2):** narrow the zoom range
  (`map-interop.js:22-23`, currently `minZoom: 7 … maxZoom: 18`) if the app never uses the
  extremes, cap `maxTileCacheSize`, and consider not loading a heavy vector basemap when
  `IsStreetMapEnabled` is off. **Note `appsettings.json:30`: the "LightOff" style points at a
  real MapTiler `style.json`, not a blank canvas** — so "off" still downloads and caches a
  full vector basemap. A genuinely empty style (`{ "version": 8, "sources": {}, "layers": [
  {background} ] }`) for the off state would cut MapLibre's tile/glyph/sprite cache to near
  zero and is a strong test of how much §0.2 contributes.

**Run this in the prod console first to attribute the bytes:**

```js
// Requires cross-origin isolation; gives a per-type byte breakdown incl. WebGL/canvas.
await performance.measureUserAgentSpecificMemory();
// Quick MapLibre-only probe:
const m = ChefMap.maps[Object.keys(ChefMap.maps)[0]];
console.log('tile cache zoom range', m.getMinZoom(), m.getMaxZoom());
console.log('routes feature count', ChefMap._routesFeatureCollection?.features?.length);
```

### Option G — Stop shipping `Debug` logging to production (addresses §0a)

`appsettings.json:21` sets `Logging.LogLevel.Default = "Debug"` and `Program.cs:87` calls
`builder.Logging.SetMinimumLevel(LogLevel.Debug)`. The animator/synth/map-interop `console.*`
calls and the .NET hot-path `LogDebug` calls then run every batch/frame, and the browser
console **retains references** to every logged object. Set prod to `Information`/`Warning`
and gate the JS `_log`/`console.log` calls behind a debug flag. Cheap, and removes a constant
aggravator — though unlikely to be the bulk of 1.2 GB by itself.

### Option A — Time-based vehicle eviction (addresses 3.1 + 3.2; robustness, NOT the 1.2 GB)

> Demoted: the flat symptom means this is **not** the cause of the current number. Still
> worth doing so the footprint stays flat over very long sessions and across fleet churn.

On each non-stale batch in `processNearestPointBatch`, stamp `state.lastSeenMs = now`. After
processing a batch (or on a low-frequency timer), sweep `this.vehicles` and delete any entry
whose `lastSeenMs` is older than a TTL (e.g. a few minutes — long enough to survive the
`MAX_EXTRAPOLATION_MS = 30000` window at `vehicle-animator.js:11`, short enough to drop
out-of-service buses). **Critically, mirror the same eviction into
`CheckpointTracker._vehicleState`** (e.g. expose a `forget(vehicleId)` on the tracker, or
have the tracker prune any `vehicleId` no longer present in `ChefMapAnimator.vehicles` at
the top of its tick hook) so the two maps stay consistent (3.2).

*Risk:* evicting a vehicle that briefly drops out of the feed and returns causes a
re-baseline (teleport instead of smooth animation). Choose TTL accordingly.

### Option B — Reduce per-frame churn (addresses 3.3)

- Skip `setData` when nothing visible changed (e.g. all vehicles idle and unmoved since the
  last frame), rather than rebuilding a FeatureCollection every RAF.
- Optionally exclude `idle`-phase vehicles from the rendered FeatureCollection entirely.
- On the .NET side, collapse the double `.ToArray()` in `HandleVehicleBatchAsync`
  (`TransitMap.razor.cs:404–410`) into a single pass to cut transient allocations per batch.

### Option C — Bound `_pendingBatches` (addresses 3.4)

Cap the buffer (keep only the most recent N batches — stale position data is worthless once
superseded) and/or add a watchdog that logs/recovers if the map hasn't signaled ready within
a timeout. Especially worth doing given the mobile focus of the current branch.

### Option D — Baseline reduction (addresses 3.6, separate effort)

WASM trimming / AOT / lazy assembly loading. Large, orthogonal effort; only pursue if the
fixed baseline (not the leaks) is the actual complaint.

---

## 6. How to confirm before acting (revised for the flat ~1.2 GB symptom)

The number is **flat**, so the goal is to **attribute the steady-state 1.2 GB**, not hunt a
leak. Do these in order:

1. **Is it a Debug build?** Confirm whether the measured app is `bin/Debug` (it appears to
   be). Publish `dotnet publish -c Release`, serve that, and re-measure. Record both numbers.
   This is step one — a large fraction of the gap may evaporate here (§0.1 / Option E).
2. **Split JS heap vs. WASM heap.** In DevTools → Memory, take a heap snapshot — that's the
   **JS** heap (MapLibre, animator, route GeoJSON). Separately, Blazor exposes WASM linear
   memory size; compare. This tells you which heap owns the 1.2 GB and therefore whether the
   fix is build/trimming (§0.1) or MapLibre/geometry (§0.2/3.7).
3. **Attribute MapLibre's share (§0.2).** Pan/zoom across the full `minZoom 7 … maxZoom 18`
   range and watch the number; if it jumps with zoom/viewport and stays elevated, MapLibre's
   tile/glyph cache is a major share. Try lowering `maxZoom` / `maxTileCacheSize` and
   re-measure.
4. **Attribute route geometry (§3.7).** In the JS heap snapshot, look at the retained size of
   `ChefMap._routesFeatureCollection` and `ChefMapAnimator.routeGeometry`; on the .NET side,
   `_routeShapeCache`. Sum them to see how much the 3–4× duplication costs.
5. **Confirm flatness (sanity check the symptom).** Leave it 30–60 min on live data; the
   number should stay ~flat. If `ChefMapAnimator.vehicles` key count *does* climb materially
   while RAM stays flat, the vehicle growth is being absorbed by GC headroom — note it but
   keep prioritizing steady-state (§0).

**Falsification:** if the Release build + MapLibre-cache tuning bring the number down to the
150–300 MB range, this whole document's leak section (§3.1–3.5) is confirmed irrelevant to
the symptom and only §0/§3.6/§3.7 mattered.

---

## 7. Files referenced

- `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/wwwroot/js/vehicle-animator.js`
- `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/wwwroot/js/checkpoint-tracker.js`
- `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/wwwroot/js/checkpoint-pulse.js`
- `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/wwwroot/js/transit-synth.js`
- `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/wwwroot/js/map-interop.js`
- `src/Client/ChefKnifeStudios.MartaJazz.Client.WebApp/Pages/TransitMap.razor.cs`
- `src/Client/ChefKnifeStudios.MartaJazz.Client.Core/Services/SignalRNotificationService.cs`
