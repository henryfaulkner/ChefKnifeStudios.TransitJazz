// checkpoint-tracker.js — crossing detection for 009-transit-soundscape
// Consumes per-tick position events from ChefMapAnimator; dispatches CrossingEvent batches to C#.

const _routeTriggerPoints = new Map();  // routeId → TriggerPoint[]
const _vehicleState = new Map();         // vehicleId → { routeId, lastTriggeredDistanceM, lastTriggerTimeMs }
let _dotNetRef = null;
let _tickHookInstalled = false;

const COOLDOWN_MS = 2000;
// Teleport threshold: > 2km along cumDist in one tick is treated as a GPS glitch
const TELEPORT_DIST_M = 2000;

function log(msg) {
    console.log('[CheckpointTracker] ' + msg);
}

export function configureRoute(routeId, triggerPoints, dotNetRef) {
    _routeTriggerPoints.set(routeId, triggerPoints);

    if (dotNetRef && !_dotNetRef) {
        _dotNetRef = dotNetRef;
    }

    if (!_tickHookInstalled) {
        _installTickHook();
        _tickHookInstalled = true;
        log('tick hook installed');
    }
}

export function clear() {
    _routeTriggerPoints.clear();
    _vehicleState.clear();
    _dotNetRef = null;
    _tickHookInstalled = false;
    _uninstallTickHook();
    log('cleared');
}

// Projects pos [lon, lat] onto a route's cumDist array, returning the along-route distance in metres.
// Uses the same nearest-vertex approach as the animator but then linearly interpolates within the
// winning segment so the result is continuous rather than quantised to vertex boundaries.
function _alongDistanceM(pos, coords, cumDist) {
    let minDist = Infinity;
    let minIdx = 0;
    for (let i = 0; i < coords.length; i++) {
        const dx = coords[i][0] - pos[0];
        const dy = coords[i][1] - pos[1];
        const d = dx * dx + dy * dy;  // squared — only need relative order
        if (d < minDist) { minDist = d; minIdx = i; }
    }

    // Interpolate within the segment that contains the nearest vertex to get a
    // sub-vertex distance rather than snapping to the vertex itself.
    // Check the segment before and after minIdx; pick whichever is closer.
    let bestD = cumDist[minIdx];

    for (const segStart of [minIdx - 1, minIdx]) {
        const segEnd = segStart + 1;
        if (segStart < 0 || segEnd >= coords.length) continue;

        const ax = coords[segStart][0], ay = coords[segStart][1];
        const bx = coords[segEnd][0],   by = coords[segEnd][1];
        const dx = bx - ax, dy = by - ay;
        const lenSq = dx * dx + dy * dy;
        if (lenSq === 0) continue;

        const t = Math.max(0, Math.min(1, ((pos[0] - ax) * dx + (pos[1] - ay) * dy) / lenSq));
        const segLen = cumDist[segEnd] - cumDist[segStart];
        const candidate = cumDist[segStart] + t * segLen;

        // Pick the candidate closest to minIdx's cumDist (avoids jumping to wrong segment)
        if (Math.abs(candidate - cumDist[minIdx]) < Math.abs(bestD - cumDist[minIdx])) {
            bestD = candidate;
        }
    }

    return bestD;
}

// Called at end of each ChefMapAnimator tick with vehicles that moved this frame.
// positionEvents: Array<{ vehicleId: string, routeId: string, currDistM: number }>
export function onTick(positionEvents) {
    if (!positionEvents || positionEvents.length === 0) return;
    if (!_dotNetRef) return;

    const batch = [];
    const now = performance.now();

    for (const ev of positionEvents) {
        const { vehicleId, routeId, currDistM } = ev;
        const triggers = _routeTriggerPoints.get(routeId);
        if (!triggers || triggers.length === 0) continue;  // FR-011: route geometry not yet loaded

        const state = _vehicleState.get(vehicleId);

        if (!state) {
            // FR-009: first observation — baseline at current distance, fire nothing
            _vehicleState.set(vehicleId, {
                routeId,
                lastTriggeredDistanceM: currDistM,
                lastTriggerTimeMs: 0,
            });
            continue;
        }

        if (state.routeId !== routeId) {
            // Vehicle transferred routes — reset, fire nothing
            state.routeId = routeId;
            state.lastTriggeredDistanceM = currDistM;
            state.lastTriggerTimeMs = 0;
            continue;
        }

        const delta = currDistM - state.lastTriggeredDistanceM;

        if (delta <= 0) continue;  // no forward movement or direction reversal

        // FR-010: teleport check
        if (delta > TELEPORT_DIST_M) {
            state.lastTriggeredDistanceM = currDistM;
            state.lastTriggerTimeMs = 0;
            continue;
        }

        // Find trigger points in (lastTriggeredDistanceM, currDistM]
        for (const tp of triggers) {
            if (tp.alongDistanceM > state.lastTriggeredDistanceM && tp.alongDistanceM <= currDistM) {
                // FR-007: cooldown suppression
                if ((now - state.lastTriggerTimeMs) >= COOLDOWN_MS) {
                    batch.push({ vehicleId, routeId, triggerIndex: tp.index, totalTriggers: triggers.length });
                    state.lastTriggerTimeMs = now;
                }
            }
        }

        state.lastTriggeredDistanceM = currDistM;
    }

    if (batch.length > 0) {
        // Sort per contracts/interop-surface.md: (routeId, vehicleId, triggerIndex)
        batch.sort((a, b) =>
            a.routeId.localeCompare(b.routeId) ||
            a.vehicleId.localeCompare(b.vehicleId) ||
            a.triggerIndex - b.triggerIndex
        );
        _dotNetRef.invokeMethodAsync('OnCrossingsAsync', batch).catch(err => {
            console.warn('[CheckpointTracker] OnCrossingsAsync failed:', err);
        });
    }
}

let _originalTick = null;

function _installTickHook() {
    if (!window.ChefMapAnimator) return;
    _originalTick = window.ChefMapAnimator.tick.bind(window.ChefMapAnimator);

    window.ChefMapAnimator.tick = function (now) {
        _originalTick(now);

        // Build position events for vehicles that have a current position and moved
        const positionEvents = [];
        const vehicles = window.ChefMapAnimator.vehicles;
        for (const vehicleId of Object.keys(vehicles)) {
            const state = vehicles[vehicleId];
            if (!state || state.phase === 'idle') continue;
            if (!state.currentPos || !state.routeId) continue;

            const routeGeom = window.ChefMapAnimator.routeGeometry[state.routeId];
            if (!routeGeom) continue;

            const currDistM = _alongDistanceM(state.currentPos, routeGeom.coords, routeGeom.cumDist);
            positionEvents.push({ vehicleId: state.vehicleId, routeId: state.routeId, currDistM });
        }

        if (positionEvents.length > 0) {
            window.CheckpointTracker.onTick(positionEvents);
        }
    }.bind(window.ChefMapAnimator);
}

function _uninstallTickHook() {
    if (window.ChefMapAnimator && _originalTick) {
        window.ChefMapAnimator.tick = _originalTick;
        _originalTick = null;
    }
}

window.CheckpointTracker = { configureRoute, clear, onTick };
