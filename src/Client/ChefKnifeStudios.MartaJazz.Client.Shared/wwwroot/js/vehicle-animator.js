window.ChefMapAnimator = {
    vehicles: {},
    routeGeometry: {},
    _source: null,
    _map: null,
    _animFrameId: null,
    _running: false,
    _lastFrameLogTime: 0,
    _lastRenderMs: 0,

    // Persistent render objects (B1): one Feature per vehicle, reused across renders and
    // mutated in place, plus one array handed to setData every time. This eliminates the
    // ~5k-objects-per-render allocation/GC churn — the geojson source still re-tiles on
    // setData, but we no longer generate garbage feeding it. Keyed by vehicleId.
    _featureById: {},
    _featuresArray: [],
    // Rebuilt from _featureById only when the vehicle SET changes (add/evict), not every
    // frame. Coordinate/property mutation happens in place every render regardless.
    _featureArrayDirty: false,

    // Cap the FeatureCollection rebuild + setData upload at ~15fps. Position math still
    // runs every RAF frame (~60fps), so motion stays continuous and exact; we just stop
    // re-uploading the whole vehicles source 60×/s. Transit dots move slowly enough that
    // 15fps reads as smooth gliding, and at NYMTA scale (~5k vehicles) the full-source
    // setData is the dominant per-frame cost — throttling it is what unblocks the jank.
    RENDER_INTERVAL_MS: 1000 / 15,

    HISTORY_SIZE: 4,
    MAX_EXTRAPOLATION_MS: 30000,
    // Vehicles not seen in a batch for this long are dropped from the dict, which
    // otherwise grows monotonically with every unique vehicleId ever observed.
    // Comfortably longer than MAX_EXTRAPOLATION_MS so an animating bus is never evicted.
    EVICT_AFTER_MS: 120000,

    _log: function (level, msg, data) {
        if (data !== undefined) {
            console[level]('[ChefMapAnimator] ' + msg, data);
        } else {
            console[level]('[ChefMapAnimator] ' + msg);
        }
    },

    // --- Provider-agnostic math (verbatim from vehicle-animator.js) ---

    haversineMeters: function (p1, p2) {
        var R = 6371000;
        var toRad = Math.PI / 180;
        var dLat = (p2[1] - p1[1]) * toRad;
        var dLon = (p2[0] - p1[0]) * toRad;
        var lat1 = p1[1] * toRad;
        var lat2 = p2[1] * toRad;
        var a = Math.sin(dLat / 2) * Math.sin(dLat / 2) +
            Math.cos(lat1) * Math.cos(lat2) * Math.sin(dLon / 2) * Math.sin(dLon / 2);
        return R * 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a));
    },

    buildCumulativeDistances: function (coords) {
        var cumDist = [0];
        for (var i = 1; i < coords.length; i++) {
            cumDist.push(cumDist[i - 1] + this.haversineMeters(coords[i - 1], coords[i]));
        }
        return cumDist;
    },

    findNearestIndex: function (coords, point) {
        var minDist = Infinity;
        var minIdx = 0;
        for (var i = 0; i < coords.length; i++) {
            var d = this.haversineMeters(coords[i], point);
            if (d < minDist) {
                minDist = d;
                minIdx = i;
            }
        }
        return minIdx;
    },

    extractSubPath: function (routeCoords, startIdx, endIdx) {
        if (startIdx <= endIdx) {
            return routeCoords.slice(startIdx, endIdx + 1);
        }
        // Bus is moving backward along the polyline — take the short reversed slice.
        // The old wrap-around path shot the bus to the route terminus and back.
        return routeCoords.slice(endIdx, startIdx + 1).reverse();
    },

    interpolateAlongPath: function (subPath, cumDist, t) {
        if (subPath.length === 0) return null;
        if (subPath.length === 1 || t <= 0) return subPath[0];
        if (t >= 1) return subPath[subPath.length - 1];

        var totalDist = cumDist[cumDist.length - 1];
        var targetDist = t * totalDist;

        var lo = 0;
        var hi = cumDist.length - 1;
        while (lo < hi - 1) {
            var mid = (lo + hi) >> 1;
            if (cumDist[mid] <= targetDist) lo = mid;
            else hi = mid;
        }

        var segStart = cumDist[lo];
        var segEnd = cumDist[hi];
        var segLen = segEnd - segStart;
        var segFrac = segLen > 0 ? (targetDist - segStart) / segLen : 0;

        var lon = subPath[lo][0] + (subPath[hi][0] - subPath[lo][0]) * segFrac;
        var lat = subPath[lo][1] + (subPath[hi][1] - subPath[lo][1]) * segFrac;
        return [lon, lat];
    },

    // Empirical speed from the history ring buffer: total polyline distance / total time.
    // Falls back to the GTFS-RT speed field, then to 0.
    computeEmpiricalSpeed: function (state) {
        var hist = state.history;
        if (!hist || hist.length < 2) return state.speed || 0;

        var routeData = this.routeGeometry[state.routeJoinKey];
        if (!routeData) {
            // No polyline — fall back to straight-line distance through history.
            var firstSL = hist[0];
            var lastSL = hist[hist.length - 1];
            var dtSL = (lastSL.timeMs - firstSL.timeMs) / 1000;
            if (dtSL <= 0) return state.speed || 0;
            return this.haversineMeters(firstSL.pos, lastSL.pos) / dtSL;
        }

        var totalDist = 0;
        for (var i = 1; i < hist.length; i++) {
            var idxA = this.findNearestIndex(routeData.coords, hist[i - 1].pos);
            var idxB = this.findNearestIndex(routeData.coords, hist[i].pos);
            var lo = Math.min(idxA, idxB);
            var hi = Math.max(idxA, idxB);
            totalDist += routeData.cumDist[hi] - routeData.cumDist[lo];
        }

        var dtSec = (hist[hist.length - 1].timeMs - hist[0].timeMs) / 1000;
        if (dtSec <= 0) return state.speed || 0;

        return totalDist / dtSec;
    },

    extrapolateAlongRoute: function (state, elapsedMs) {
        var routeData = this.routeGeometry[state.routeJoinKey];
        if (!routeData) return state.currentPos;

        var speed = state.empiricalSpeed != null ? state.empiricalSpeed : (state.speed || 0);
        if (speed <= 0) return state.currentPos;

        var extraDist = speed * (elapsedMs / 1000);

        var startIdx = this.findNearestIndex(routeData.coords, state.extrapolateFromPos || state.endPos);

        var remaining = extraDist;
        for (var i = startIdx; i < routeData.coords.length - 1; i++) {
            var segDist = routeData.cumDist[i + 1] - routeData.cumDist[i];
            if (remaining <= segDist) {
                var frac = remaining / segDist;
                var lon = routeData.coords[i][0] + (routeData.coords[i + 1][0] - routeData.coords[i][0]) * frac;
                var lat = routeData.coords[i][1] + (routeData.coords[i + 1][1] - routeData.coords[i][1]) * frac;
                return [lon, lat];
            }
            remaining -= segDist;
        }

        this._log('debug', 'extrapolateAlongRoute: reached end of route for vehicle ' + state.vehicleId);
        return routeData.coords[routeData.coords.length - 1];
    },

    loadRouteGeometry: function (routeJoinKey, coordinates) {
        var cumDist = this.buildCumulativeDistances(coordinates);
        this.routeGeometry[routeJoinKey] = { coords: coordinates, cumDist: cumDist };
        this._log('debug', 'loadRouteGeometry: ' + routeJoinKey + ' (' + coordinates.length + ' coords, ' + Math.round(cumDist[cumDist.length - 1]) + 'm total)');
    },

    start: function () {
        if (this._running) return;
        this._log('info', 'animation loop starting');
        this._running = true;
        this._animFrameId = requestAnimationFrame(this.tick.bind(this));
    },

    stop: function () {
        this._log('info', 'animation loop stopping');
        this._running = false;
        if (this._animFrameId) {
            cancelAnimationFrame(this._animFrameId);
            this._animFrameId = null;
        }
    },

    // --- MapLibre-specific tick: builds FeatureCollection, calls setData once per frame ---

    tick: function (now) {
        if (!this._running) return;

        if (!this._source) {
            this._log('warn', 'tick: source not set, skipping frame');
            this._animFrameId = requestAnimationFrame(this.tick.bind(this));
            return;
        }

        var vehicleIds = Object.keys(this.vehicles);
        var activeCount = 0;
        var extrapolatingCount = 0;
        var idleCount = 0;

        // Gate the render (FeatureCollection rebuild + setData) to ~15fps. Position math
        // below still runs every frame so state.currentPos is always current; we only skip
        // the expensive full-source upload — and the per-vehicle feature allocation — on
        // frames between render intervals.
        var shouldRender = (now - this._lastRenderMs) >= this.RENDER_INTERVAL_MS;

        for (var i = 0; i < vehicleIds.length; i++) {
            var state = this.vehicles[vehicleIds[i]];
            if (!state) continue;

            // Evict vehicles the feed has stopped reporting so the dict can't grow
            // unbounded across a long session.
            if (state.lastSeenMs != null && (now - state.lastSeenMs) > this.EVICT_AFTER_MS) {
                delete this.vehicles[vehicleIds[i]];
                if (this._featureById[vehicleIds[i]]) {
                    delete this._featureById[vehicleIds[i]];
                    this._featureArrayDirty = true; // vehicle set changed → rebuild array
                }
                continue;
            }

            var newPos = state.currentPos;

            if (state.phase === 'idle') {
                idleCount++;
            } else {
                activeCount++;
                var elapsed = now - state.startTime;

                if (state.phase === 'interpolating') {
                    var t = Math.min(elapsed / state.duration, 1.0);
                    var interpolated = this.interpolateAlongPath(state.subPath, state.subPathCumDist, t);
                    if (interpolated) newPos = interpolated;

                    if (t >= 1.0) {
                        state.endPos = newPos;
                        var hasSpeed = (state.empiricalSpeed > 0) || (state.speed > 0);
                        var nextPhase = hasSpeed ? 'extrapolating' : 'idle';
                        this._log('debug', 'vehicle ' + state.vehicleId + ': interpolation complete → ' + nextPhase);
                        state.phase = nextPhase;
                        state.startTime = now;
                        state.extrapolateFromPos = newPos;
                    }
                } else if (state.phase === 'extrapolating') {
                    extrapolatingCount++;
                    newPos = this.extrapolateAlongRoute(state, elapsed);
                    if (elapsed > this.MAX_EXTRAPOLATION_MS) {
                        this._log('debug', 'vehicle ' + state.vehicleId + ': extrapolation timeout → idle');
                        state.phase = 'idle';
                    }
                }

                state.currentPos = newPos;
            }

            if (shouldRender) {
                // Reuse this vehicle's persistent Feature, mutating geometry/properties in
                // place instead of allocating a fresh object every render (B1). New vehicle →
                // create once and mark the array dirty so it gets rebuilt below.
                var feature = this._featureById[state.vehicleId];
                if (!feature) {
                    feature = {
                        type: 'Feature',
                        id: 'vehicle-' + state.vehicleId,
                        geometry: { type: 'Point', coordinates: newPos },
                        properties: {
                            vehicleId: state.vehicleId,
                            pinIcon: 'stop-pin-green',
                            routeJoinKey: state.routeJoinKey,
                            transitMode: state.transitMode,
                            bearing: state.bearing
                        }
                    };
                    this._featureById[state.vehicleId] = feature;
                    this._featureArrayDirty = true;
                } else {
                    feature.geometry.coordinates = newPos;
                    // Properties can change between batches (route transfer, bearing), so
                    // keep them current — cheap primitive assignment, no allocation.
                    var p = feature.properties;
                    p.routeJoinKey = state.routeJoinKey;
                    p.transitMode = state.transitMode;
                    p.bearing = state.bearing;
                }
            }
        }

        // Single setData call per render interval (~15fps) rather than every RAF frame —
        // the full-source upload is the dominant per-frame cost at scale (R1). The array and
        // its Feature objects are persistent (B1); we only rebuild the array when the vehicle
        // set changed, and setData reads the mutated-in-place coordinates.
        if (shouldRender) {
            this._lastRenderMs = now;
            if (this._featureArrayDirty) {
                this._featuresArray = Object.keys(this._featureById).map(function (id) {
                    return this._featureById[id];
                }, this);
                this._featureArrayDirty = false;
            }
            this._source.setData({ type: 'FeatureCollection', features: this._featuresArray });
        }

        if (now - this._lastFrameLogTime >= 1000) {
            this._lastFrameLogTime = now;
            this._log('debug', 'tick summary', {
                total: vehicleIds.length,
                active: activeCount,
                extrapolating: extrapolatingCount,
                idle: idleCount
            });
        }

        this._animFrameId = requestAnimationFrame(this.tick.bind(this));
    },

    // --- MapLibre-specific processNearestPointBatch (R4 touch points applied) ---

    processNearestPointBatch: function (containerDivId, records) {
        this._log('debug', 'processNearestPointBatch: received ' + records.length + ' records for map ' + containerDivId);

        var map = ChefMap.maps[containerDivId];
        if (!map) {
            this._log('warn', 'processNearestPointBatch: map not found for containerDivId=' + containerDivId);
            return;
        }

        var source = map.getSource('vehicles');
        if (!source) {
            this._log('warn', 'processNearestPointBatch: vehicles source not found');
            return;
        }

        this._source = source;
        this._map = map;
        if (!this._running) this.start();

        var now = performance.now();
        var newVehicles = 0;
        var updatedVehicles = 0;
        var teleportedVehicles = 0;
        var fallbackLerpVehicles = 0;

        var staleVehicles = 0;
        var unchangedMovingVehicles = 0;

        for (var i = 0; i < records.length; i++) {
            var rec = records[i];
            var existingState = this.vehicles[rec.vehicleId];
            // Every record that references a vehicle refreshes its eviction stamp,
            // regardless of which branch below handles it.
            if (existingState) existingState.lastSeenMs = now;

            // Route transfer — teleport, don't animate
            if (existingState && existingState.routeJoinKey !== rec.routeJoinKey) {
                this._log('debug', 'vehicle ' + rec.vehicleId + ': route transfer ' + existingState.routeJoinKey + ' → ' + rec.routeJoinKey + ', teleporting');
                teleportedVehicles++;
                existingState = null;
            }

            // Stale upstream sample: GTFS-RT delivered the same per-vehicle timestamp.
            // Don't pollute history or rebuild subPath — just re-anchor the extrapolator
            // to the bus's current rendered position and keep using empirical speed.
            // If we have no prior state, ignore the stale record entirely (we'd be
            // creating a vehicle with no real motion data).
            if (rec.isStale && existingState) {
                staleVehicles++;
                existingState.startTime = now;
                existingState.extrapolateFromPos = existingState.currentPos;
                // Preserve phase if already extrapolating; otherwise promote to
                // extrapolating when we have a usable empirical speed.
                if (existingState.phase !== 'extrapolating' && existingState.empiricalSpeed > 0) {
                    existingState.phase = 'extrapolating';
                }
                continue;
            }
            if (rec.isStale && !existingState) {
                // No prior client state to extrapolate from, but the record still
                // carries a valid current position. This is the cold-start case: the
                // warm last-batch snapshot is almost entirely stale records, so dropping
                // them left the map empty until the first fresh SignalR batch arrived.
                // Place the bus at its current position as an idle vehicle; the next
                // live (non-stale) batch will pick it up and begin animating.
                staleVehicles++;
                var staleStartPos = [rec.currentLon, rec.currentLat];
                this.vehicles[rec.vehicleId] = {
                    vehicleId: rec.vehicleId,
                    routeJoinKey: rec.routeJoinKey,
                    subPath: [staleStartPos],
                    subPathCumDist: [0],
                    totalDistance: 0,
                    startTime: now,
                    duration: rec.durationMs || 10000,
                    speed: rec.speed || null,
                    empiricalSpeed: 0,
                    bearing: rec.bearing || null,
                    currentPos: staleStartPos,
                    endPos: staleStartPos,
                    extrapolateFromPos: staleStartPos,
                    history: [],
                    phase: 'idle',
                    lastSeenMs: now
                };
                newVehicles++;
                continue;
            }

            // "Unchanged" outcome: a fresh report whose snap point matches the prior
            // snap but speed > 0. The bus is moving, just didn't cross a polyline
            // vertex boundary this cycle. Treat like Stale for the animator: keep
            // extrapolating from current rendered position, but do NOT push the
            // duplicate snap into history (would falsely teach empirical speed that
            // the bus traveled 0m in 10s and drag it toward zero).
            // Stationary (speed == 0, same snap) falls through to the normal path
            // so the zero-speed sample correctly enters history and idles the bus.
            var isUnchangedMoving = existingState
                && rec.priorLat === rec.currentLat
                && rec.priorLon === rec.currentLon
                && (rec.speed || 0) > 0;

            if (isUnchangedMoving) {
                unchangedMovingVehicles++;
                existingState.startTime = now;
                existingState.extrapolateFromPos = existingState.currentPos;
                if (existingState.phase !== 'extrapolating' && existingState.empiricalSpeed > 0) {
                    existingState.phase = 'extrapolating';
                }
                continue;
            }

            var routeData = this.routeGeometry[rec.routeJoinKey];
            var subPath, subPathCumDist, totalDistance;
            var duration = rec.durationMs || 10000;

            if (routeData) {
                var startIdx = this.findNearestIndex(routeData.coords, [rec.priorLon, rec.priorLat]);
                var endIdx = this.findNearestIndex(routeData.coords, [rec.currentLon, rec.currentLat]);
                this._log('debug', 'vehicle ' + rec.vehicleId + ': route=' + rec.routeJoinKey + ' startIdx=' + startIdx + ' endIdx=' + endIdx);
                subPath = this.extractSubPath(routeData.coords, startIdx, endIdx);
                subPathCumDist = this.buildCumulativeDistances(subPath);
                totalDistance = subPathCumDist[subPathCumDist.length - 1];
            } else {
                fallbackLerpVehicles++;
                subPath = [[rec.priorLon, rec.priorLat], [rec.currentLon, rec.currentLat]];
                subPathCumDist = this.buildCumulativeDistances(subPath);
                totalDistance = subPathCumDist[1] || 0;
            }

            var startPos = existingState ? existingState.currentPos : [rec.priorLon, rec.priorLat];

            // Update ring-buffer history of recent snap points. Stored as
            // { pos: [lon, lat], timeMs: clientArrivalTime } — we use client time so
            // empirical speed accounts for actual rendering cadence, not server jitter.
            var history = (existingState && existingState.history) ? existingState.history.slice() : [];
            if (history.length === 0) {
                history.push({ pos: [rec.priorLon, rec.priorLat], timeMs: now - duration });
            }
            history.push({ pos: [rec.currentLon, rec.currentLat], timeMs: now });
            while (history.length > this.HISTORY_SIZE) history.shift();

            // Smooth handoff: if mid-animation, start from current rendered position
            if (existingState && existingState.phase !== 'idle' && subPath.length > 1) {
                this._log('debug', 'vehicle ' + rec.vehicleId + ': mid-animation handoff from ' + JSON.stringify(startPos));
                subPath[0] = startPos;
                subPathCumDist = this.buildCumulativeDistances(subPath);
                totalDistance = subPathCumDist[subPathCumDist.length - 1];
            }

            // Compute empirical speed from history. This is more stable than the
            // GTFS-RT speed field and lets us extrapolate forward smoothly even on
            // "unchanged snap" batches.
            var tempState = {
                routeJoinKey: rec.routeJoinKey,
                speed: rec.speed || 0,
                history: history
            };
            var empiricalSpeed = this.computeEmpiricalSpeed(tempState);

            // Phase selection. With a usable speed signal we prefer extrapolation:
            // the snapped subPath can drift faster than reality, so trust speed *
            // wallclock when we have it. Interpolation kicks in only when we have a
            // real subPath but no speed (early-life, stopped vehicle reporting null).
            var hasSpeed = empiricalSpeed > 0;
            var phase;
            if (hasSpeed) {
                phase = 'extrapolating';
            } else if (totalDistance > 0) {
                phase = 'interpolating';
            } else {
                phase = 'idle';
            }

            this._log('debug', 'vehicle ' + rec.vehicleId + ': ' + phase
                + ', subPathDist=' + Math.round(totalDistance) + 'm'
                + ', duration=' + Math.round(duration) + 'ms'
                + ', empSpeed=' + empiricalSpeed.toFixed(2) + 'm/s'
                + ', gtfsSpeed=' + (rec.speed || 0).toFixed(2) + 'm/s'
                + ', histLen=' + history.length);

            if (!existingState) {
                newVehicles++;
            } else {
                updatedVehicles++;
            }

            this.vehicles[rec.vehicleId] = {
                vehicleId: rec.vehicleId,
                routeJoinKey: rec.routeJoinKey,
                transitMode: rec.transitMode || 'bus',
                subPath: subPath,
                subPathCumDist: subPathCumDist,
                totalDistance: totalDistance,
                startTime: now,
                duration: duration,
                speed: rec.speed || null,
                empiricalSpeed: empiricalSpeed,
                bearing: rec.bearing || null,
                currentPos: startPos,
                endPos: subPath[subPath.length - 1],
                extrapolateFromPos: startPos,
                history: history,
                phase: phase,
                lastSeenMs: now
            };
        }

        this._log('info', 'processNearestPointBatch complete', {
            records: records.length,
            newVehicles: newVehicles,
            updated: updatedVehicles,
            teleported: teleportedVehicles,
            stale: staleVehicles,
            unchangedMoving: unchangedMovingVehicles,
            fallbackLerp: fallbackLerpVehicles
        });
    }
};
