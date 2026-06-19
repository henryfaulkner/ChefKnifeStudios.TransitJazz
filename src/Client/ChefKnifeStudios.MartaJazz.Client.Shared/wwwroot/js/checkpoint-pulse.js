// Checkpoint pulse overlay — expanding, route-colored ring animation.
// Exported API: ensureLayer(map), start(map, routeId, triggerIndex, coordinates, color), reset(map)

const SOURCE_ID = 'checkpoint-pulse';
const LAYER_ID = 'checkpoint-pulse-layer';

const DURATION_MS = 600;
const R_START = 3;
const R_END = 14;
const O_START = 0.6;

// keyed "{routeId}::{triggerIndex}" → { coordinates, color, startTimeMs }
const _activePulses = new Map();
let _rafHandle = null;

function _easeOutCubic(t) {
    return 1 - Math.pow(1 - t, 3);
}

function _tick(map) {
    const now = performance.now();
    const features = [];

    for (const [key, pulse] of _activePulses) {
        const t = Math.min(1, (now - pulse.startTimeMs) / DURATION_MS);
        const eased = _easeOutCubic(t);
        const radius = R_START + eased * (R_END - R_START);
        const opacity = O_START * (1 - t);

        features.push({
            type: 'Feature',
            geometry: { type: 'Point', coordinates: pulse.coordinates },
            properties: { radius, color: pulse.color, opacity }
        });

        if (t >= 1) _activePulses.delete(key);
    }

    try {
        const src = map.getSource(SOURCE_ID);
        if (src) src.setData({ type: 'FeatureCollection', features });
    } catch (_) { }

    if (_activePulses.size > 0) {
        _rafHandle = requestAnimationFrame(() => _tick(map));
    } else {
        _rafHandle = null;
    }
}

export function ensureLayer(map) {
    if (map.getSource(SOURCE_ID)) return;

    map.addSource(SOURCE_ID, {
        type: 'geojson',
        data: { type: 'FeatureCollection', features: [] }
    });

    const beforeLayer = map.getLayer('vehicles-layer') ? 'vehicles-layer'
        : map.getLayer('trigger-points-layer') ? 'trigger-points-layer'
        : undefined;

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
    }, beforeLayer);
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
    if (_rafHandle !== null) {
        cancelAnimationFrame(_rafHandle);
        _rafHandle = null;
    }
    try {
        const src = map.getSource(SOURCE_ID);
        if (src) src.setData({ type: 'FeatureCollection', features: [] });
    } catch (_) { }
}
