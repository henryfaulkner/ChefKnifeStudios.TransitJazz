let _checkpointPulseModule = null;
async function _getCheckpointPulse() {
    if (!_checkpointPulseModule) {
        _checkpointPulseModule = await import('/_content/ChefKnifeStudios.TransitJazz.Client.Shared/js/checkpoint-pulse.js');
    }
    return _checkpointPulseModule;
}

let _checkpointTrailModule = null;
async function _getCheckpointTrail() {
    if (!_checkpointTrailModule) {
        _checkpointTrailModule = await import('/_content/ChefKnifeStudios.TransitJazz.Client.Shared/js/checkpoint-trail.js');
    }
    return _checkpointTrailModule;
}

window.ChefMap = {
    maps: {},
    _visibilityHandlers: {},
    _routesFeatureCollection: null,

    createMap: async function (containerDivId, dotNetRef, generation) {
        let settings = await dotNetRef.invokeMethodAsync('getMapSettings');

        // Keep unsupported WebGL distinct from a transient style/network initialization failure.
        if (typeof maplibregl !== 'undefined'
            && typeof maplibregl.supported === 'function'
            && !maplibregl.supported()) {
            await dotNetRef.invokeMethodAsync('notifyMapUnsupportedBrowserAsync', generation);
            return;
        }

        let map = new maplibregl.Map({
            container: containerDivId,
            style: settings.styleUrl,
            center: settings.center,
            zoom: settings.zoom,
            minZoom: 7,
            maxZoom: 18,
            dragRotate: false
        });

        // Pinch-to-zoom on, touch rotation off — keeps the map north-up (FR-003, FR-007).
        map.touchZoomRotate.enable();
        map.touchZoomRotate.disableRotation();

        // On-screen zoom +/− buttons at bottom-left; compass omitted (no rotation — FR-007).
        // Bottom-right is occupied by fixed-position audio and map-style FABs.
        map.addControl(new maplibregl.NavigationControl({ showCompass: false, showZoom: true, visualizePitch: false }), 'bottom-left');

        ChefMap.maps[containerDivId] = map;
        map._chefMapGeneration = generation;

        const visibilityHandler = function () {
            dotNetRef.invokeMethodAsync(
                'notifyMapVisibilityAsync', generation, document.visibilityState === 'visible')
                .catch(error => console.debug('[ChefMap] visibility callback ended', error));
        };
        ChefMap._visibilityHandlers[containerDivId] = visibilityHandler;
        document.addEventListener('visibilitychange', visibilityHandler);
        visibilityHandler();

        // Ctrl+drag: pan instead of rotate (dragRotate is disabled above)
        let ctrlDragStart = null;
        map.getCanvas().addEventListener('mousedown', function (e) {
            if (e.ctrlKey) ctrlDragStart = { x: e.clientX, y: e.clientY };
        });
        map.getCanvas().addEventListener('mousemove', function (e) {
            if (ctrlDragStart && e.buttons === 1 && e.ctrlKey) {
                map.panBy([ctrlDragStart.x - e.clientX, ctrlDragStart.y - e.clientY], { animate: false });
                ctrlDragStart = { x: e.clientX, y: e.clientY };
            } else if (!e.ctrlKey || e.buttons !== 1) {
                ctrlDragStart = null;
            }
        });
        map.getCanvas().addEventListener('mouseup', function () { ctrlDragStart = null; });

        map.on('load', function () {
            try {
            // Vehicles GeoJSON source + circle layer — must exist before the animator calls getSource('vehicles')
            map.addSource('vehicles', {
                type: 'geojson',
                data: { type: 'FeatureCollection', features: [] }
            });

            map.addLayer({
                id: 'vehicles-layer',
                type: 'circle',
                source: 'vehicles',
                layout: { 'visibility': 'none' },
                paint: {
                    'circle-radius': ['match', ['downcase', ['get', 'category']], 'rail', 9, 6],
                    'circle-color': '#22c55e',
                    'circle-stroke-width': ['match', ['downcase', ['get', 'category']], 'rail', 2, 1],
                    'circle-stroke-color': '#fff'
                }
            });

            // Vehicle click → BusMarkerClickedAsync
            map.on('click', 'vehicles-layer', function (e) {
                if (e.features && e.features.length > 0) {
                    let vehicleId = e.features[0].properties.vehicleId;
                    dotNetRef.invokeMethodAsync('BusMarkerClickedAsync', String(vehicleId));
                }
            });

            map.on('mouseenter', 'vehicles-layer', function () {
                map.getCanvas().style.cursor = 'pointer';
            });

            map.on('mouseleave', 'vehicles-layer', function () {
                map.getCanvas().style.cursor = '';
            });

            // Empty-area click → mapBodyClickedAsync
            map.on('click', function (e) {
                let features = map.queryRenderedFeatures(e.point, { layers: ['vehicles-layer'] });
                if (!features || features.length === 0) {
                    dotNetRef.invokeMethodAsync('mapBodyClickedAsync');
                }
            });

            // Pre-create pulse layers so setCheckpointVisibility works before the first crossing.
            _getCheckpointPulse()
                .then(function (pulse) { pulse.ensureLayer(map); })
                .catch(error => console.debug('[ChefMap] optional pulse layer unavailable', error));

            // Pre-create the crossing-trail layer so the first crossing renders immediately.
            _getCheckpointTrail()
                .then(function (trail) { trail.ensureLayer(map); })
                .catch(error => console.debug('[ChefMap] optional crossing-trail layer unavailable', error));

            let containerDiv = document.getElementById(containerDivId);
            const requiredContentAvailable = !!containerDiv
                && !!map.getSource('vehicles')
                && !!map.getLayer('vehicles-layer');
            console.debug('[ChefMap] map load complete, checking required content for ' + containerDivId);
            dotNetRef.invokeMethodAsync(
                'notifyMapInitializationAsync', generation, requiredContentAvailable)
                .catch(error => console.warn('[ChefMap] map readiness callback failed', error));
            } catch (error) {
                console.error('[ChefMap] required map initialization failed for ' + containerDivId, error);
                dotNetRef.invokeMethodAsync('notifyMapInitializationAsync', generation, false)
                    .catch(callbackError => console.warn('[ChefMap] map failure callback failed', callbackError));
            }
        });
    },

    removeMap: function (containerDivId) {
        const visibilityHandler = ChefMap._visibilityHandlers[containerDivId];
        if (visibilityHandler) {
            document.removeEventListener('visibilitychange', visibilityHandler);
            delete ChefMap._visibilityHandlers[containerDivId];
        }
        const map = ChefMap.maps[containerDivId];
        if (map) {
            map.remove();
            delete ChefMap.maps[containerDivId];
        }
    },

    setMapZoom: function (containerDivId, zoom) {
        let map = ChefMap.maps[containerDivId];
        if (!map) return;
        map.setZoom(zoom);
    },

    setMapStyle: function (containerDivId, styleUrl, generation, operationGeneration) {
        const map = ChefMap.maps[containerDivId];
        const emptyResult = {
            requestedStyleLoaded: false,
            previousRequiredSourcesAndLayersRestored: false,
            mapUsable: false,
            terminalFailure: true,
            checkpointVisible: null,
            generation: -1,
            operationGeneration: operationGeneration
        };
        if (!map || map._chefMapGeneration !== generation)
            return Promise.resolve(emptyResult);

        let previousStyle;
        let vehiclesData = { type: 'FeatureCollection', features: [] };
        try {
            previousStyle = map.getStyle();
            const vehicleSource = map.getSource('vehicles');
            if (vehicleSource) vehiclesData = vehicleSource._data || vehiclesData;
        } catch (error) {
            console.warn('[ChefMap] setMapStyle could not snapshot the current map');
            return Promise.resolve(emptyResult);
        }

        return new Promise(function (resolve) {
            let completed = false;
            let restoringPreviousStyle = false;
            let requestTimer = null;
            let restoreTimer = null;

            function cleanup() {
                if (requestTimer) clearTimeout(requestTimer);
                if (restoreTimer) clearTimeout(restoreTimer);
                map.off('style.load', onRequestedStyleLoaded);
                map.off('style.load', onPreviousStyleLoaded);
            }

            function requiredLayersAvailable() {
                try {
                    return map._chefMapGeneration === generation
                        && map.isStyleLoaded()
                        && !!map.getSource('vehicles')
                        && !!map.getLayer('vehicles-layer')
                        && (!ChefMap._routesFeatureCollection
                            || (!!map.getSource('routes') && !!map.getLayer('routes-layer')));
                } catch (error) {
                    return false;
                }
            }

            function checkpointVisibility() {
                try { return map.getLayoutProperty('trigger-points-layer', 'visibility') || null; }
                catch (error) { return null; }
            }

            function restoreRequiredContent() {
                let restored = true;
                try {
                    if (!map.getSource('vehicles'))
                        map.addSource('vehicles', { type: 'geojson', data: vehiclesData });
                    else
                        map.getSource('vehicles').setData(vehiclesData);
                    if (!map.getLayer('vehicles-layer')) {
                        map.addLayer({
                            id: 'vehicles-layer',
                            type: 'circle',
                            source: 'vehicles',
                            layout: { 'visibility': 'none' },
                            paint: {
                                'circle-radius': ['match', ['downcase', ['get', 'category']], 'rail', 9, 6],
                                'circle-color': '#22c55e',
                                'circle-stroke-width': ['match', ['downcase', ['get', 'category']], 'rail', 2, 1],
                                'circle-stroke-color': '#fff'
                            }
                        });
                    }
                } catch (error) {
                    restored = false;
                    console.warn('[ChefMap] setMapStyle could not restore required vehicle content');
                }

                try {
                    if (ChefMap._routesFeatureCollection) {
                        if (!map.getSource('routes'))
                            map.addSource('routes', { type: 'geojson', data: ChefMap._routesFeatureCollection });
                        else
                            map.getSource('routes').setData(ChefMap._routesFeatureCollection);
                        if (!map.getLayer('routes-layer')) {
                            map.addLayer({
                                id: 'routes-layer',
                                type: 'line',
                                source: 'routes',
                                layout: { 'line-join': 'round', 'line-cap': 'round' },
                                paint: {
                                    'line-color': '#6b7280',
                                    'line-width': 2,
                                    'line-opacity': 0.7
                                }
                            }, 'vehicles-layer');
                        }
                    }
                } catch (error) {
                    restored = false;
                    console.warn('[ChefMap] setMapStyle could not restore required route content');
                }

                // Checkpoint and trail overlays are optional; their failures do not
                // turn an otherwise usable transit map into a root capability failure.
                try {
                    if (!map.getSource('trigger-points')) {
                        const features = Object.values(ChefMap._triggerPointFeatures).flat();
                        map.addSource('trigger-points', {
                            type: 'geojson',
                            data: { type: 'FeatureCollection', features: features }
                        });
                    }
                    if (!map.getLayer('trigger-points-layer')) {
                        map.addLayer({
                            id: 'trigger-points-layer',
                            type: 'circle',
                            source: 'trigger-points',
                            layout: { visibility: 'none' },
                            paint: {
                                'circle-radius': 4,
                                'circle-color': ['coalesce', ['get', 'color'], '#facc15'],
                                'circle-opacity': 0.85,
                                'circle-stroke-width': 1,
                                'circle-stroke-color': '#000000'
                            }
                        });
                    }
                } catch (error) {
                    console.debug('[ChefMap] optional checkpoint content remains unavailable');
                }
                _getCheckpointPulse().then(function (pulse) {
                    pulse.ensureLayer(map);
                    pulse.reset(map);
                }).catch(function () { });
                _getCheckpointTrail().then(function (trail) {
                    trail.ensureLayer(map);
                }).catch(function () { });
                return restored && requiredLayersAvailable();
            }

            function finish(requestedStyleLoaded, previousRestored, terminalFailure) {
                if (completed) return;
                completed = true;
                cleanup();
                const usable = requiredLayersAvailable();
                resolve({
                    requestedStyleLoaded: requestedStyleLoaded,
                    previousRequiredSourcesAndLayersRestored: previousRestored && usable,
                    mapUsable: usable,
                    terminalFailure: terminalFailure || (!usable && !restoringPreviousStyle),
                    checkpointVisible: checkpointVisibility(),
                    generation: map._chefMapGeneration,
                    operationGeneration: operationGeneration
                });
            }

            function onRequestedStyleLoaded() {
                if (completed || restoringPreviousStyle) return;
                if (requestTimer) clearTimeout(requestTimer);
                restoreRequiredContent();
                finish(true, false, false);
            }

            function onPreviousStyleLoaded() {
                if (completed || !restoringPreviousStyle) return;
                if (restoreTimer) clearTimeout(restoreTimer);
                const restored = restoreRequiredContent();
                const usable = requiredLayersAvailable();
                finish(false, restored && usable, !usable);
            }

            function beginPreviousStyleRestore() {
                if (completed || restoringPreviousStyle) return;
                restoringPreviousStyle = true;
                if (requestTimer) clearTimeout(requestTimer);
                map.off('style.load', onRequestedStyleLoaded);
                map.once('style.load', onPreviousStyleLoaded);
                restoreTimer = setTimeout(function () {
                    // The timeout starts verification; without a style.load and required
                    // content readback, restoration remains unverified and is not M4.
                    finish(false, false, false);
                }, 4000);
                try {
                    map.setStyle(previousStyle, { diff: false });
                } catch (error) {
                    finish(false, false, false);
                }
            }

            map.once('style.load', onRequestedStyleLoaded);
            requestTimer = setTimeout(beginPreviousStyleRestore, 4000);
            try {
                map.setStyle(styleUrl, { diff: false });
            } catch (error) {
                beginPreviousStyleRestore();
            }
        });
    },

    processNearestPointBatch: function (containerDivId, records, generation, operationGeneration) {
        const map = ChefMap.maps[containerDivId];
        function outcome(rendered, terminalFailure, actualGeneration) {
            return {
                rendered: rendered,
                terminalFailure: terminalFailure,
                generation: actualGeneration,
                operationGeneration: operationGeneration
            };
        }

        if (!map || map._chefMapGeneration !== generation)
            return Promise.resolve(outcome(false, false, -1));
        if (!map.isStyleLoaded())
            return Promise.resolve(outcome(false, false, generation));
        if (!map.getSource('vehicles') || !map.getLayer('vehicles-layer'))
            return Promise.resolve(outcome(false, true, generation));

        if (!window.ChefMapAnimator
            || !window.ChefMapAnimator.processNearestPointBatch(containerDivId, records))
            return Promise.resolve(outcome(false, !window.ChefMapAnimator, generation));

        return new Promise(function (resolve) {
            let completed = false;
            let timer = null;
            function finish(rendered, terminalFailure = false) {
                if (completed) return;
                completed = true;
                if (timer) clearTimeout(timer);
                map.off('render', onRendered);
                const stillReady = !!rendered
                    && map._chefMapGeneration === generation
                    && map.isStyleLoaded()
                    && !!map.getSource('vehicles')
                    && !!map.getLayer('vehicles-layer');
                resolve(outcome(stillReady, terminalFailure, generation));
            }
            function onRendered() { finish(true); }
            map.once('render', onRendered);
            timer = setTimeout(function () { finish(false); }, 2000);
            map.triggerRepaint();
        });
    },
    centerVehiclePin: function (containerDivId, vehicleId) {
        let map = ChefMap.maps[containerDivId];
        if (!map) return;

        let state = ChefMapAnimator.vehicles[vehicleId];
        if (state && state.currentPos) {
            map.easeTo({ center: state.currentPos });
        }
    },

    plotFeatures: function (containerDivId, sourceId, featureCollection, centerMap) {
        let map = ChefMap.maps[containerDivId];
        if (!map) return;

        let source = map.getSource(sourceId);
        if (!source) {
            map.addSource(sourceId, { type: 'geojson', data: featureCollection });
            map.addLayer({
                id: sourceId + '-layer',
                type: 'circle',
                source: sourceId,
                paint: { 'circle-radius': 6, 'circle-color': '#22c55e' }
            });
            return;
        }

        source.setData(featureCollection);

        if (centerMap && featureCollection.features && featureCollection.features.length > 0) {
            try {
                let coords = featureCollection.features
                    .filter(f => f.geometry && f.geometry.type === 'Point')
                    .slice(0, 20)
                    .map(f => f.geometry.coordinates);

                if (coords.length > 0) {
                    let bounds = coords.reduce(function (b, c) {
                        return b.extend(c);
                    }, new maplibregl.LngLatBounds(coords[0], coords[0]));
                    map.fitBounds(bounds, { padding: 40, maxZoom: 14 });
                }
            } catch (e) { }
        }
    },

    // Coordinate lookup for pulse targeting — routeJoinKey → Feature[].
    // Also pushed to the 'trigger-points' source for the all-checkpoints overlay.
    _triggerPointFeatures: {},

    // --- Checkpoint geometry helpers (place/identify checkpoints by along-route distance) ---

    _haversineM: function (p1, p2) {
        var R = 6371000, toRad = Math.PI / 180;
        var dLat = (p2[1] - p1[1]) * toRad, dLon = (p2[0] - p1[0]) * toRad;
        var a = Math.sin(dLat / 2) * Math.sin(dLat / 2) +
            Math.cos(p1[1] * toRad) * Math.cos(p2[1] * toRad) * Math.sin(dLon / 2) * Math.sin(dLon / 2);
        return R * 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a));
    },

    _cumulativeDistances: function (coords) {
        var cum = [0];
        for (var i = 1; i < coords.length; i++) cum.push(cum[i - 1] + ChefMap._haversineM(coords[i - 1], coords[i]));
        return cum;
    },

    // Point at `targetDistM` along the polyline (interpolated within the containing segment).
    _interpolateAlongCoords: function (coords, cumDist, targetDistM) {
        if (coords.length === 0) return null;
        if (targetDistM <= 0) return coords[0];
        var total = cumDist[cumDist.length - 1];
        if (targetDistM >= total) return coords[coords.length - 1];
        var lo = 0, hi = cumDist.length - 1;
        while (lo < hi - 1) {
            var mid = (lo + hi) >> 1;
            if (cumDist[mid] <= targetDistM) lo = mid; else hi = mid;
        }
        var segLen = cumDist[hi] - cumDist[lo];
        var f = segLen > 0 ? (targetDistM - cumDist[lo]) / segLen : 0;
        return [coords[lo][0] + (coords[hi][0] - coords[lo][0]) * f,
                coords[lo][1] + (coords[hi][1] - coords[lo][1]) * f];
    },

    // Resolve a checkpoint feature by its along-route distance — the STABLE identity, unlike
    // triggerIndex which collides on sparse polylines. Tolerance absorbs float round-trip
    // (server sends metres; checkpoints are spaced far wider than 1m).
    _findCheckpointFeature: function (routeJoinKey, alongDistanceM) {
        var features = ChefMap._triggerPointFeatures[routeJoinKey];
        if (!features) return null;
        var best = null, bestDelta = Infinity;
        for (var i = 0; i < features.length; i++) {
            var delta = Math.abs(features[i].properties.alongDistanceM - alongDistanceM);
            if (delta < bestDelta) { bestDelta = delta; best = features[i]; }
        }
        return (best && bestDelta <= 1.0) ? best : null;
    },

    addTriggerPointMarkers: function (containerDivId, routeJoinKey, triggerPoints, coords) {
        var map = ChefMap.maps[containerDivId];
        if (!map) return;

        var routeColor = ChefMap._routeColorsByRouteJoinKey[routeJoinKey] || '#facc15';

        // Cumulative distance along the polyline, so each checkpoint can be placed at its TRUE
        // along-route distance. tp.index is the nearest polyline VERTEX, which collides when the
        // shape is sparse (many 400m checkpoints snap to one vertex → identical coords + a
        // non-unique key). Interpolating by alongDistanceM gives every checkpoint a distinct
        // position and lets alongDistanceM serve as the stable identity for pulse/trail lookups.
        var cumDist = ChefMap._cumulativeDistances(coords);

        // Accumulate this route's features — do NOT rebuild/flush the combined collection here.
        // flushTriggerPoints() does a single combined setData after all routes are added.
        ChefMap._triggerPointFeatures[routeJoinKey] = triggerPoints.map(function (tp) {
            var coord = ChefMap._interpolateAlongCoords(coords, cumDist, tp.alongDistanceM);
            return {
                type: 'Feature',
                geometry: { type: 'Point', coordinates: coord },
                properties: { routeJoinKey: routeJoinKey, triggerIndex: tp.index, alongDistanceM: tp.alongDistanceM, color: routeColor }
            };
        });

        // Ensure the source + layer exist (with empty data) so visibility toggles work
        // before the first flush. No per-route data push here.
        if (!map.getSource('trigger-points')) {
            map.addSource('trigger-points', { type: 'geojson', data: { type: 'FeatureCollection', features: [] } });
            map.addLayer({
                id: 'trigger-points-layer',
                type: 'circle',
                source: 'trigger-points',
                layout: { visibility: 'none' },
                paint: {
                    'circle-radius': 4,
                    'circle-color': ['coalesce', ['get', 'color'], '#facc15'],
                    'circle-opacity': 0.85,
                    'circle-stroke-width': 1,
                    'circle-stroke-color': '#000000'
                }
            });
        }
    },

    // Build the combined trigger-point FeatureCollection once and push it to the map.
    // Called exactly once after all routes have been added via addTriggerPointMarkers.
    flushTriggerPoints: function (containerDivId) {
        var map = ChefMap.maps[containerDivId];
        if (!map) return;

        var allFeatures = Object.values(ChefMap._triggerPointFeatures).flat();
        var fc = { type: 'FeatureCollection', features: allFeatures };

        var source = map.getSource('trigger-points');
        if (!source) {
            map.addSource('trigger-points', { type: 'geojson', data: fc });
            map.addLayer({
                id: 'trigger-points-layer',
                type: 'circle',
                source: 'trigger-points',
                layout: { visibility: 'none' },
                paint: {
                    'circle-radius': 4,
                    'circle-color': ['coalesce', ['get', 'color'], '#facc15'],
                    'circle-opacity': 0.85,
                    'circle-stroke-width': 1,
                    'circle-stroke-color': '#000000'
                }
            });
        } else {
            source.setData(fc);
        }
    },

    setAllCheckpointsVisibility: function (containerDivId, visible) {
        let map = ChefMap.maps[containerDivId];
        if (!map) return;
        if (!map.getLayer('trigger-points-layer')) return;
        map.setLayoutProperty('trigger-points-layer', 'visibility', visible ? 'visible' : 'none');
    },

    _routeColorsByRouteJoinKey: {},  // routeJoinKey → data color, for vehicle dot coloring
    _routeCategoryByRouteJoinKey: {},  // routeJoinKey → vehicle category (rail/bus/streetcar/…), for checkpoint coloring
    _isDarkMode: false,  // updated on every addAllRoutes push; drives checkpoint light/dark palette

    // Checkpoint pulse + crossing-trail colors mirror the vehicle-category swatches in
    // TransitRunningLabel.razor (the "vehicle label type" chips), not the route line color.
    // Keep these in sync with that component's light/dark [data-category] CSS.
    _categoryColors: {
        light: { rail: '#1a237e', bus: '#006400', streetcar: '#b71c1c', _default: '#616161' },
        dark:  { rail: '#7986cb', bus: '#66bb6a', streetcar: '#e57373', _default: '#9e9e9e' }
    },

    // Vehicle-category resolution for the v2 wire (feature 051, US4 / FR-013).
    //
    // Distinct from _routeCategoryByRouteJoinKey above, which defaults absent categories to
    // 'bus' for checkpoint coloring. Vehicle rendering MUST NOT guess: v2 omits the category
    // for routes the client can resolve itself, so an absent entry here means "genuinely not
    // classified" and has to stay 'unknown' — that is the data-quality signal FR-013 protects.
    // Returns null when the catalog has no entry, letting the caller decide the fallback.
    _vehicleCategoryByRouteJoinKey: {},

    getVehicleCategory: function (routeJoinKey) {
        let c = ChefMap._vehicleCategoryByRouteJoinKey[routeJoinKey];
        return c ? c : null;
    },

    // Resolve the checkpoint color for a route from its vehicle category + current theme,
    // falling back to the neutral default (never the route line color).
    _checkpointColorFor: function (routeJoinKey) {
        let palette = ChefMap._isDarkMode ? ChefMap._categoryColors.dark : ChefMap._categoryColors.light;
        let category = (ChefMap._routeCategoryByRouteJoinKey[routeJoinKey] || '').toLowerCase();
        return palette[category] || palette._default;
    },

    _applyVehicleRouteColors: function (containerDivId) {
        let map = ChefMap.maps[containerDivId];
        if (!map || !map.getLayer('vehicles-layer')) return;

        let entries = Object.entries(ChefMap._routeColorsByRouteJoinKey);
        if (entries.length === 0) return;

        // Build a MapLibre match expression: ['match', ['get', 'routeJoinKey'], id, color, ..., fallback]
        let matchExpr = ['match', ['get', 'routeJoinKey']];
        entries.forEach(function (pair) {
            matchExpr.push(pair[0], pair[1]);
        });
        matchExpr.push('#6b7280');  // fallback for unknown routeJoinKey

        map.setPaintProperty('vehicles-layer', 'circle-color', matchExpr);
    },

    focusRoutes: function (containerDivId, routeIds) {
        let map = ChefMap.maps[containerDivId];
        if (!map || !map.getLayer('routes-layer')) return;

        let selected = new Set(routeIds || []);

        // Build match expressions: selected routes get their route color at full opacity;
        // all others get grey at reduced opacity.
        let colorExpr = ['match', ['get', 'routeJoinKey']];
        let opacityExpr = ['match', ['get', 'routeJoinKey']];
        Object.entries(ChefMap._routeColorsByRouteJoinKey).forEach(function ([rid, color]) {
            colorExpr.push(rid, selected.has(rid) ? color : '#9ca3af');
            opacityExpr.push(rid, selected.has(rid) ? 0.95 : 0.35);
        });
        colorExpr.push('#9ca3af');   // fallback
        opacityExpr.push(0.35);      // fallback

        map.setPaintProperty('routes-layer', 'line-color', colorExpr);
        map.setPaintProperty('routes-layer', 'line-opacity', opacityExpr);
    },

    focusRoute: function (containerDivId, routeJoinKey) {
        ChefMap.focusRoutes(containerDivId, [routeJoinKey]);
    },

    clearRouteFocus: function (containerDivId) {
        let map = ChefMap.maps[containerDivId];
        if (!map || !map.getLayer('routes-layer')) return;

        // No selection — all routes grey at default opacity.
        map.setPaintProperty('routes-layer', 'line-color', '#6b7280');
        map.setPaintProperty('routes-layer', 'line-opacity', 0.7);
    },

    // Checkpoints are identified by alongDistanceM (stable geometry), NOT triggerIndex, which
    // collides when multiple checkpoints snap to one sparse-polyline vertex. triggerIndex is
    // still passed through as the pulse's internal per-checkpoint state key.
    pulseCheckpoint: async function (containerDivId, routeJoinKey, triggerIndex, alongDistanceM) {
        let map = ChefMap.maps[containerDivId];
        if (!map) return;

        let feature = ChefMap._findCheckpointFeature(routeJoinKey, alongDistanceM);
        if (!feature) {
            console.warn('[ChefMap] pulseCheckpoint: no checkpoint at alongDistanceM=' + alongDistanceM + ' for routeJoinKey=' + routeJoinKey);
            return;
        }

        let color = ChefMap._checkpointColorFor(routeJoinKey);
        let coords = feature.geometry.coordinates;

        try {
            let pulse = await _getCheckpointPulse();
            pulse.ensureLayer(map);
            pulse.start(map, routeJoinKey, triggerIndex, coords, color);
        } catch (e) {
            console.error('[ChefMap] pulseCheckpoint: error — ', e);
        }
    },

    setCheckpointVisibility: async function (containerDivId, visible) {
        let map = ChefMap.maps[containerDivId];
        if (!map) return;

        try {
            let pulse = await _getCheckpointPulse();
            pulse.setVisible(map, visible);
        } catch (e) {
            console.warn('[ChefMap] setCheckpointVisibility: pulse layer error — ' + e);
        }
    },

    startCrossingTrail: async function (containerDivId, routeJoinKey, vehicleId, triggerIndex, durationSec, alongDistanceM) {
        let map = ChefMap.maps[containerDivId];
        if (!map) return;

        // Anchor by alongDistanceM (same stable identity as pulseCheckpoint), not triggerIndex.
        let feature = ChefMap._findCheckpointFeature(routeJoinKey, alongDistanceM);
        if (!feature) return;

        let anchorCoord = feature.geometry.coordinates;
        let anchorDistanceM = feature.properties.alongDistanceM;
        let color = ChefMap._checkpointColorFor(routeJoinKey);   // vehicle-category swatch, not route color

        // Speed: read empirical speed from the animator (audio-independent — R5).
        let vstate = ChefMapAnimator.vehicles[vehicleId];
        let speedMps = (vstate && (vstate.empiricalSpeed ?? vstate.speed)) || 0;

        try {
            let trail = await _getCheckpointTrail();
            trail.ensureLayer(map);
            trail.start(map, routeJoinKey, vehicleId, triggerIndex, anchorCoord, anchorDistanceM, color, speedMps, durationSec);
        } catch (e) {
            console.error('[ChefMap] startCrossingTrail: error — ', e);
        }
    },

    setCrossingTrailVisibility: async function (containerDivId, visible) {
        let map = ChefMap.maps[containerDivId];
        if (!map) return;
        try {
            let trail = await _getCheckpointTrail();
            trail.setVisible(map, visible);          // false → reset() clears active trails (FR-006)
        } catch (e) {
            console.warn('[ChefMap] setCrossingTrailVisibility: trail layer error — ' + e);
        }
    },

    setVehiclesVisible: function (containerDivId, visible) {
        let map = ChefMap.maps[containerDivId];
        if (!map) return;
        if (!map.getLayer('vehicles-layer')) return;
        map.setLayoutProperty('vehicles-layer', 'visibility', visible ? 'visible' : 'none');
    },

    addAllRoutes: function (containerDivId, routes, isDarkMode) {
        let map = ChefMap.maps[containerDivId];
        if (!map) {
            console.warn('[ChefMap] addAllRoutes: no map for containerDivId=' + containerDivId);
            return;
        }

        ChefMap._isDarkMode = !!isDarkMode;

        let features = [];
        (routes || []).forEach(function (route) {
            if (!route.coordinates || route.coordinates.length === 0) return;

            let lineColor = route.color || '#6b7280';

            ChefMap._routeColorsByRouteJoinKey[route.routeJoinKey] = lineColor;
            ChefMap._routeCategoryByRouteJoinKey[route.routeJoinKey] = route.category || 'bus';
            // Vehicle-category catalog: store only a real category — no 'bus' default (see
            // getVehicleCategory). This is the seam FR-013a's re-resolution hooks below.
            if (route.category) {
                ChefMap._vehicleCategoryByRouteJoinKey[route.routeJoinKey] = route.category;
            }

            ChefMapAnimator.loadRouteGeometry(route.routeJoinKey, route.coordinates);

            features.push({
                type: 'Feature',
                id: route.routeJoinKey,
                geometry: { type: 'LineString', coordinates: route.coordinates },
                properties: { routeJoinKey: route.routeJoinKey, color: lineColor }
            });
        });

        // FR-013a: the vehicle-category catalog is now populated. Vehicles delivered by the
        // JoinCity replay before this point fell back to 'unknown' purely because the catalog
        // was empty — reclassify those now that it can answer.
        if (typeof ChefMapAnimator !== 'undefined' && ChefMapAnimator.reresolveUnknownCategories) {
            ChefMapAnimator.reresolveUnknownCategories();
        }

        let fc = { type: 'FeatureCollection', features: features };
        ChefMap._routesFeatureCollection = fc;

        let source = map.getSource('routes');
        if (source) {
            source.setData(fc);
        } else {
            map.addSource('routes', { type: 'geojson', data: fc });
            map.addLayer({
                id: 'routes-layer',
                type: 'line',
                source: 'routes',
                layout: { 'line-join': 'round', 'line-cap': 'round' },
                paint: {
                    'line-color': '#6b7280',
                    'line-width': 2,
                    'line-opacity': 0.7
                }
            }, 'vehicles-layer');
        }

        ChefMap._applyVehicleRouteColors(containerDivId);
    }
};
