// Checkpoint crossing trail — transient, route-colored line that grows along the
// route from the crossed checkpoint over the note's duration, then vanishes.
// Structural sibling of checkpoint-pulse.js (same RAF loop + ensureLayer/start/reset/setVisible API).
// Exported API: ensureLayer(map), start(map, routeId, vehicleId, triggerIndex, anchorCoord, anchorDistanceM, color, speedMps, durationSec), reset(map), setVisible(map, visible)

// --- Tuning constants (FR-010) ---
const MIN_SPEED = 2.0;     // m/s  — speed floor (a stopped bus still marks)
const MAX_SPEED = 30.0;    // m/s  — speed ceiling
const LENGTH_SCALE = 60.0; //       — exaggeration factor (raw speed×duration is sub-meter;
                           //         scale up so a 12px round-capped line reads as a streak
                           //         rather than two overlapping end-cap dots)
const MAX_LEN_M = 600;     // m    — hard length cap
const TRAIL_WIDTH = 4;    // px   — line width (matches bus dot diameter)

const SOURCE_ID = 'crossing-trail';
const LAYER_ID = 'crossing-trail-layer';

// keyed "{vehicleId}::{triggerIndex}::{startTimeMs}" → { routeId, color, anchorDistanceM, finalLengthM, durationMs, startTimeMs }
const _activeTrails = new Map();
let _rafHandle = null;
let _lastRenderMs = 0;

// Gate the rebuild + setData to ~15fps. This loop is the heaviest of the three: _tick rebuilds
// a LineString per active trail, and _buildLineCoords walks the ENTIRE route polyline each time
// (O(active_trails × route_vertices)). At NYMTA scale a crossing burst stacks many trails on
// long subway lines, so throttling to 15fps cuts that O(n) walk 4× for free. The head-distance
// is a pure function of `now`, so a growing line looks identical at 15fps.
const RENDER_INTERVAL_MS = 1000 / 15;

function clamp(v, lo, hi) {
    return Math.min(hi, Math.max(lo, v));
}

// Interpolate a [lon, lat] coordinate at distance `d` (meters) along the route,
// given its coords + cumulative-distance arrays. Mirrors the animator's segment lerp.
function _coordAtDistance(coords, cumDist, d) {
    const total = cumDist[cumDist.length - 1];
    if (d <= 0) return coords[0];
    if (d >= total) return coords[coords.length - 1];

    let lo = 0;
    let hi = cumDist.length - 1;
    while (lo < hi - 1) {
        const mid = (lo + hi) >> 1;
        if (cumDist[mid] <= d) lo = mid;
        else hi = mid;
    }
    const segLen = cumDist[hi] - cumDist[lo];
    const frac = segLen > 0 ? (d - cumDist[lo]) / segLen : 0;
    const lon = coords[lo][0] + (coords[hi][0] - coords[lo][0]) * frac;
    const lat = coords[lo][1] + (coords[hi][1] - coords[lo][1]) * frac;
    return [lon, lat];
}

// Build a LineString's coordinates by slicing the route between two along-distances,
// interpolating both endpoints within their containing segments.
function _buildLineCoords(coords, cumDist, tailDistM, headDistM) {
    const out = [_coordAtDistance(coords, cumDist, tailDistM)];
    for (let i = 0; i < coords.length; i++) {
        if (cumDist[i] > tailDistM && cumDist[i] < headDistM) out.push(coords[i]);
    }
    out.push(_coordAtDistance(coords, cumDist, headDistM));
    return out;
}

function _tick(map) {
    const now = performance.now();
    const shouldRender = (now - _lastRenderMs) >= RENDER_INTERVAL_MS;

    if (shouldRender) {
        _lastRenderMs = now;
        const features = [];

        for (const [key, trail] of _activeTrails) {
            const t = clamp((now - trail.startTimeMs) / trail.durationMs, 0, 1);

            // Immediate removal when the note ends (FR-004 / SC-002) — next setData excludes it.
            if (t >= 1) {
                _activeTrails.delete(key);
                continue;
            }

            const routeData = ChefMapAnimator.routeGeometry[trail.routeId];
            if (!routeData) continue;

            const routeTotalLengthM = routeData.cumDist[routeData.cumDist.length - 1];
            const headDistanceM = Math.min(trail.anchorDistanceM + trail.finalLengthM * t, routeTotalLengthM);
            const lineCoords = _buildLineCoords(routeData.coords, routeData.cumDist, trail.anchorDistanceM, headDistanceM);

            features.push({
                type: 'Feature',
                geometry: { type: 'LineString', coordinates: lineCoords },
                properties: { color: trail.color }
            });
        }

        try {
            const src = map.getSource(SOURCE_ID);
            if (src) src.setData({ type: 'FeatureCollection', features });
        } catch (_) { }
    } else {
        // Non-render frame: still expire finished trails so the loop can stop on time.
        for (const [key, trail] of _activeTrails) {
            if (clamp((now - trail.startTimeMs) / trail.durationMs, 0, 1) >= 1) _activeTrails.delete(key);
        }
    }

    if (_activeTrails.size > 0) {
        _rafHandle = requestAnimationFrame(() => _tick(map));
    } else {
        _rafHandle = null;
        // Final clear: the last trail may have expired on a NON-render frame (throttle gate),
        // leaving its last-drawn line frozen on the map because that frame skipped setData.
        // Force one empty setData so no stale trail persists after the loop stops.
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

    // Growing line: route-colored, fixed width matching the bus dot. Inserted below the
    // checkpoint pulse layers (if present) so the expanding ring renders above the trail.
    const beforeId = map.getLayer('checkpoint-active-dot-layer') ? 'checkpoint-active-dot-layer' : undefined;
    if (!map.getLayer(LAYER_ID)) {
        map.addLayer({
            id: LAYER_ID,
            type: 'line',
            source: SOURCE_ID,
            layout: {
                'line-cap': 'round',
                'line-join': 'round',
                'visibility': 'visible'
            },
            paint: {
                'line-color': ['get', 'color'],
                'line-width': TRAIL_WIDTH
            }
        }, beforeId);
    }
}

export function start(map, routeId, vehicleId, triggerIndex, anchorCoord, anchorDistanceM, color, speedMps, durationSec) {
    // No geometry → can't slice a line along the route. No-op.
    if (!ChefMapAnimator.routeGeometry[routeId]) return;

    // Final length scales with speed × note duration, floored so a stopped bus still
    // marks and capped at MAX_LEN_M (FR-003; refined in US2).
    const finalLengthM = Math.min(MAX_LEN_M, clamp(speedMps, MIN_SPEED, MAX_SPEED) * durationSec * LENGTH_SCALE);

    const startTimeMs = performance.now();
    // Unique time-stamped key so concurrent crossings on the same checkpoint stack (FR-007).
    const key = `${vehicleId}::${triggerIndex}::${startTimeMs}`;
    _activeTrails.set(key, {
        routeId,
        color,
        anchorDistanceM,
        finalLengthM,
        durationMs: durationSec * 1000,
        startTimeMs
    });

    if (_rafHandle === null) {
        _rafHandle = requestAnimationFrame(() => _tick(map));
    }
}

export function reset(map) {
    _activeTrails.clear();
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
    if (!visible) reset(map);   // clears active trails immediately (FR-006)
    try {
        if (map.getLayer(LAYER_ID)) map.setLayoutProperty(LAYER_ID, 'visibility', vis);
    } catch (_) { }
}

window.CheckpointTrail = { ensureLayer, start, reset, setVisible };
