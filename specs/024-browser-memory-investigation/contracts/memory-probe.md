# Contract: `window.MemoryProbe` (in-app memory attribution)

**File**: `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/wwwroot/js/memory-probe.js`
**Loaded by**: `index.html` (already wired). Promoted from "delete-once-solved diagnostic" to a
supported attribution tool (kept, documented in quickstart).

## Surface (existing — preserved, hardened)

| Method | Returns | Guarantee |
|--------|---------|-----------|
| `await MemoryProbe.report()` | consolidated `MemoryMeasurementResult` (see data-model E1) | one call produces the full breakdown; never throws |
| `await MemoryProbe.measureUA()` | UA per-type breakdown **or** `{ error: string }` | **FR-003**: returns `{ error }` (with reason) when `measureUserAgentSpecificMemory` is unavailable or context is not `crossOriginIsolated` — MUST NOT throw |
| `MemoryProbe.wasmHeap()` | `{ wasmHeapMB }` \| `{ note }` \| `{ error }` | the .NET WASM linear-memory size; the runtime-heap share (FR-002), available without isolation |
| `MemoryProbe.jsHeap()` | `{ usedJSHeapMB, totalJSHeapMB, limitMB }` \| null | coarse Chromium JS-only number |
| `MemoryProbe.appObjects()` | object (logged via `console.table`) | rough sizes of our retained structures |
| `MemoryProbe.maplibre()` | array (logged via `console.table`) | MapLibre source/tile-cache probe |
| `MemoryProbe.watch(intervalMs=5000)` | `stop()` function | polls a compact line each interval (flatness check, FR-013) |

## Acceptance vectors

| # | Context | Call | Expected |
|---|---------|------|----------|
| A1 | Chromium, crossOriginIsolated | `await MemoryProbe.report()` | `wasmHeap.wasmHeapMB` present; `measureUserAgentSpecificMemory` returns a `breakdown` array incl. a Canvas/WebGL-attributed bucket → runtime vs. graphics split stated (SC-002) |
| A2 | Chromium, NOT isolated | `await MemoryProbe.measureUA()` | `{ error: '...not crossOriginIsolated...' }` — no throw (FR-003); `wasmHeap()` still returns a number so the WASM share is still known |
| A3 | Non-Chromium / no `measureUserAgentSpecificMemory` | `await MemoryProbe.measureUA()` | `{ error: '...unavailable (needs a recent Chromium build)' }` — no throw |
| A4 | Any | `MemoryProbe.wasmHeap()` when runtime buffer absent | `{ note: 'WASM heap buffer not found...' }` — no throw |
| A5 | Live session 30–60 min | `const stop = MemoryProbe.watch(5000)` then `stop()` | `wasm=` line stays ~flat (no upward trend) → confirms FR-013 |

## Non-goals
- No UI surface (no settings-panel control) — diagnostic only; therefore no resx string (Principle XII).
- Does not itself reduce memory; it attributes it.
