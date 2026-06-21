// memory-probe.js — ad-hoc browser-memory instrumentation for the RAM investigation.
// See docs/BROWSER_MEMORY_INVESTIGATION_DESIGN_DOCUMENT.md.
//
// This is a DIAGNOSTIC tool, not a product feature. Nothing here runs automatically;
// everything is invoked manually from the DevTools console via window.MemoryProbe.
// Safe to leave shipped (zero cost until called) or to remove once the cause is found.
//
// Quick start (paste in the console):
//   await MemoryProbe.report()      // one-shot breakdown of every heap we can see
//   MemoryProbe.appObjects()        // sizes of OUR retained data structures
//   MemoryProbe.maplibre()          // MapLibre tile-cache / source probe
//   await MemoryProbe.watch(5000)   // poll a compact summary every 5s; returns a stop() fn

window.MemoryProbe = {

    // --- Low-level heap readings (browser-provided, best-effort) -----------------

    // Per-type byte breakdown incl. WebGL/canvas. Requires a cross-origin-isolated
    // context (COOP+COEP headers). Returns null with a hint if unavailable.
    async measureUA() {
        if (!performance.measureUserAgentSpecificMemory) {
            return { error: 'performance.measureUserAgentSpecificMemory unavailable (needs a recent Chromium build)' };
        }
        if (!self.crossOriginIsolated) {
            return { error: 'not crossOriginIsolated — set COOP:same-origin + COEP:require-corp to enable the WebGL/canvas breakdown' };
        }
        try {
            return await performance.measureUserAgentSpecificMemory();
        } catch (e) {
            return { error: String(e) };
        }
    },

    // Legacy, Chromium-only, coarse JS-heap number. Always available where present;
    // does NOT include WebGL/WASM. Handy for the watch() poller.
    jsHeap() {
        const m = performance.memory;
        if (!m) return null;
        return {
            usedJSHeapMB: +(m.usedJSHeapSize / 1048576).toFixed(1),
            totalJSHeapMB: +(m.totalJSHeapSize / 1048576).toFixed(1),
            limitMB: +(m.jsHeapSizeLimit / 1048576).toFixed(1),
        };
    },

    // Blazor/.NET WASM linear-memory size, if the runtime exposes its HEAP buffer.
    wasmHeap() {
        try {
            const dn = globalThis.Blazor?.runtime?.Module
                ?? globalThis.Module
                ?? globalThis.getDotnetRuntime?.(0)?.Module;
            const buf = dn?.HEAPU8?.buffer ?? dn?.wasmMemory?.buffer;
            if (!buf) return { note: 'WASM heap buffer not found on this runtime build' };
            return { wasmHeapMB: +(buf.byteLength / 1048576).toFixed(1) };
        } catch (e) {
            return { error: String(e) };
        }
    },

    // --- Application-object sizes (rough byte estimates of OUR retained data) -----

    _roughBytes(obj) {
        // Cheap structural estimate via JSON length. Undercounts typed/number arrays'
        // true in-memory size but is good enough to rank our own structures. Guards
        // against cycles by catching the stringify throw.
        try {
            return JSON.stringify(obj)?.length ?? 0;
        } catch {
            return -1; // cyclic / non-serializable
        }
    },

    appObjects() {
        const A = window.ChefMapAnimator;
        const M = window.ChefMap;
        const mb = (b) => (b < 0 ? 'n/a (cyclic)' : +(b / 1048576).toFixed(2) + ' MB');

        const vehicleIds = A ? Object.keys(A.vehicles || {}) : [];
        const routeGeomIds = A ? Object.keys(A.routeGeometry || {}) : [];
        const routesFC = M?._routesFeatureCollection;
        const triggerFeatures = M?._triggerPointFeatures
            ? Object.values(M._triggerPointFeatures).flat()
            : [];

        const out = {
            'animator.vehicles': {
                count: vehicleIds.length,
                approxSize: mb(this._roughBytes(A?.vehicles)),
            },
            'animator.routeGeometry': {
                routes: routeGeomIds.length,
                totalCoords: routeGeomIds.reduce(
                    (n, id) => n + (A.routeGeometry[id]?.coords?.length || 0), 0),
                approxSize: mb(this._roughBytes(A?.routeGeometry)),
            },
            'ChefMap._routesFeatureCollection': {
                features: routesFC?.features?.length ?? 0,
                totalCoords: (routesFC?.features || []).reduce(
                    (n, f) => n + (f.geometry?.coordinates?.length || 0), 0),
                approxSize: mb(this._roughBytes(routesFC)),
            },
            'ChefMap._triggerPointFeatures': {
                points: triggerFeatures.length,
                approxSize: mb(this._roughBytes(M?._triggerPointFeatures)),
            },
        };
        console.table(
            Object.fromEntries(Object.entries(out).map(([k, v]) => [k, v.approxSize ? { ...v } : v]))
        );
        return out;
    },

    // --- MapLibre internals (the prime suspect: tile / glyph / sprite cache) ------

    maplibre() {
        const M = window.ChefMap;
        const ids = M ? Object.keys(M.maps || {}) : [];
        if (ids.length === 0) return { note: 'no MapLibre maps registered yet' };

        const reports = ids.map((id) => {
            const map = M.maps[id];
            const style = (() => { try { return map.getStyle(); } catch { return null; } })();
            const sources = style?.sources ? Object.keys(style.sources) : [];
            const sourceTypes = {};
            for (const s of sources) sourceTypes[s] = style.sources[s].type;

            return {
                containerId: id,
                zoom: +map.getZoom().toFixed(2),
                minZoom: map.getMinZoom(),
                maxZoom: map.getMaxZoom(),
                // Tile cache budget MapLibre keeps in memory; large maxZoom span +
                // big viewport inflates this. The actual count lives on internal
                // SourceCaches, which we probe best-effort below.
                styleSourceCount: sources.length,
                sourceTypes,
                loadedTilesApprox: this._countLoadedTiles(map),
            };
        });
        console.table(reports);
        return reports;
    },

    _countLoadedTiles(map) {
        // Best-effort: reach into the (private) style sourceCaches to total cached tiles.
        // Wrapped in try/catch because this touches MapLibre internals that can change.
        try {
            const caches = map.style?._sourceCaches ?? map.style?.sourceCaches;
            if (!caches) return 'n/a';
            let total = 0;
            for (const key of Object.keys(caches)) {
                const tiles = caches[key]?._tiles;
                if (tiles) total += Object.keys(tiles).length;
            }
            return total;
        } catch {
            return 'n/a';
        }
    },

    // --- One-shot consolidated report --------------------------------------------

    async report() {
        const r = {
            timestamp: new Date().toISOString(),
            jsHeap: this.jsHeap(),
            wasmHeap: this.wasmHeap(),
            measureUserAgentSpecificMemory: await this.measureUA(),
        };
        console.group('%c[MemoryProbe] report', 'font-weight:bold');
        console.log('JS heap (Chromium, JS-only):', r.jsHeap);
        console.log('WASM linear memory:', r.wasmHeap);
        console.log('UA breakdown (incl. WebGL/canvas):', r.measureUserAgentSpecificMemory);
        console.log('— app objects —');
        r.appObjects = this.appObjects();
        console.log('— MapLibre —');
        r.maplibre = this.maplibre();
        console.groupEnd();
        return r;
    },

    // --- Poller: watch the number move while you pan/zoom/let it run --------------

    // Returns a stop() function. Logs a compact line each interval.
    watch(intervalMs = 5000) {
        const id = setInterval(() => {
            const js = this.jsHeap();
            const wasm = this.wasmHeap();
            const vehicles = Object.keys(window.ChefMapAnimator?.vehicles || {}).length;
            console.log(
                `[MemoryProbe] js=${js?.usedJSHeapMB ?? '?'}MB`
                + ` wasm=${wasm?.wasmHeapMB ?? '?'}MB`
                + ` vehicles=${vehicles}`
                + ` @${new Date().toLocaleTimeString()}`
            );
        }, intervalMs);
        console.log('[MemoryProbe] watch started — call the returned stop() to end');
        const stop = () => { clearInterval(id); console.log('[MemoryProbe] watch stopped'); };
        return stop;
    },
};

console.info('[MemoryProbe] loaded — try: await MemoryProbe.report()');
