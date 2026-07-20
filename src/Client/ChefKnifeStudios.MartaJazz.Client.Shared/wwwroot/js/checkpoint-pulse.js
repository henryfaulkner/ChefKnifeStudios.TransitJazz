// Checkpoint pulse overlay — expanding, route-colored ring animation.
// Exported API: ensureLayer(map), start(map, routeId, triggerIndex, coordinates, color), reset(map)

const SOURCE_ID = 'checkpoint-pulse';
const LAYER_ID = 'checkpoint-pulse-layer';
const DOT_LAYER_ID = 'checkpoint-active-dot-layer';
const DOT_RADIUS = 4;

const DURATION_MS = 600;
const R_START = 3;
const R_END = 14;
const O_START = 0.6;

// keyed "{routeId}::{triggerIndex}" → { coordinates, color, startTimeMs }
const _activePulses = new Map();
let _rafHandle = null;
let _lastRenderMs = 0;

// Gate the FeatureCollection rebuild + setData to ~15fps. At NYMTA scale a crossing burst
// can stack hundreds of concurrent pulses, and re-tiling the whole source 60×/s (once here,
// again in the trail loop, again in the vehicle animator) stalls the main thread. The eased
// radius/opacity is a pure function of `now`, so 15fps looks identical for a 600ms ring.
const RENDER_INTERVAL_MS = 1000 / 15;

function _easeOutCubic(t) {
    return 1 - Math.pow(1 - t, 3);
}

function _tick(map) {
    const now = performance.now();
    const shouldRender = (now - _lastRenderMs) >= RENDER_INTERVAL_MS;

    // Expiry runs every frame (cheap) so the loop terminates promptly when the last pulse
    // ends; only the expensive feature build + setData is gated to the render interval.
    if (shouldRender) {
        _lastRenderMs = now;
        const features = [];

        for (const [key, pulse] of _activePulses) {
            const t = Math.min(1, (now - pulse.startTimeMs) / DURATION_MS);

            if (t >= 1) {
                _activePulses.delete(key);
                continue;
            }

            const eased = _easeOutCubic(t);
            const radius = R_START + eased * (R_END - R_START);
            const opacity = O_START * (1 - t);

            features.push({
                type: 'Feature',
                geometry: { type: 'Point', coordinates: pulse.coordinates },
                properties: { radius, color: pulse.color, opacity }
            });
        }

        try {
            const src = map.getSource(SOURCE_ID);
            if (src) src.setData({ type: 'FeatureCollection', features });
        } catch (_) { }
    } else {
        // Non-render frame: still expire finished pulses so the loop can stop on time.
        for (const [key, pulse] of _activePulses) {
            if ((now - pulse.startTimeMs) / DURATION_MS >= 1) _activePulses.delete(key);
        }
    }

    if (_activePulses.size > 0) {
        _rafHandle = requestAnimationFrame(() => _tick(map));
    } else {
        _rafHandle = null;
        // Final clear: the last pulse may have expired on a NON-render frame (throttle gate),
        // leaving its last-drawn ring frozen on the map because that frame skipped setData.
        // Force one empty setData so no stale pulse persists after the loop stops.
        try {
            const src = map.getSource(SOURCE_ID);
            if (src) src.setData({ type: 'FeatureCollection', features: [] });
        } catch (_) { }
    }
}

export function ensureLayer(map) {
    if (map.getSource(SOURCE_ID)) return;

    map.addSource(SOURCE_ID, {
        type: 'geojson',
        data: { type: 'FeatureCollection', features: [] }
    });

    // Active dot: solid filled circle at the checkpoint, stays fully opaque for the pulse duration.
    // No beforeLayer arg — renders on top of everything (vehicles, trigger-points, routes).
    map.addLayer({
        id: DOT_LAYER_ID,
        type: 'circle',
        source: SOURCE_ID,
        layout: { visibility: 'visible' },
        paint: {
            'circle-radius': DOT_RADIUS,
            'circle-color': ['get', 'color'],
            'circle-opacity': 1,
            'circle-stroke-width': 0.34,
            'circle-stroke-color': '#000000',
            'circle-stroke-opacity': 1
        }
    });

    // Expanding ring: grows outward and fades. Added before the dot so the dot stays on top.
    map.addLayer({
        id: LAYER_ID,
        type: 'circle',
        source: SOURCE_ID,
        layout: { visibility: 'visible' },
        paint: {
            'circle-radius': ['get', 'radius'],
            'circle-color': ['get', 'color'],
            'circle-opacity': ['get', 'opacity'],
            'circle-stroke-width': 0
        }
    }, DOT_LAYER_ID);
}

export function start(map, routeId, triggerIndex, coordinates, color) {
    const key = `${routeId}::${triggerIndex}`;
    _activePulses.set(key, { coordinates, color, startTimeMs: performance.now() });

    if (_rafHandle === null) {
        _rafHandle = requestAnimationFrame(() => _tick(map));
    }
}

export function reset(map) {
    _activePulses.clear();
    _lastRenderMs = 0; // next start() renders on its first frame
    if (_rafHandle !== null) {
        cancelAnimationFrame(_rafHandle);
        _rafHandle = null;
    }
    try {
        const src = map.getSource(SOURCE_ID);
        if (src) src.setData({ type: 'FeatureCollection', features: [] });
    } catch (_) { }
}

export function setVisible(map, visible) {
    const vis = visible ? 'visible' : 'none';
    if (!visible) reset(map);
    try {
        if (map.getLayer(DOT_LAYER_ID)) map.setLayoutProperty(DOT_LAYER_ID, 'visibility', vis);
        if (map.getLayer(LAYER_ID)) map.setLayoutProperty(LAYER_ID, 'visibility', vis);
    } catch (_) { }
}
