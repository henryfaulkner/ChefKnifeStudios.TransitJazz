let _checkpointPulseModule = null;
async function _getCheckpointPulse() {
    if (!_checkpointPulseModule) {
        _checkpointPulseModule = await import('/_content/ChefKnifeStudios.MartaJazz.Client.Shared/js/checkpoint-pulse.js');
    }
    return _checkpointPulseModule;
}

let _checkpointTrailModule = null;
async function _getCheckpointTrail() {
    if (!_checkpointTrailModule) {
        _checkpointTrailModule = await import('/_content/ChefKnifeStudios.MartaJazz.Client.Shared/js/checkpoint-trail.js');
    }
    return _checkpointTrailModule;
}

window.ChefMap = {
    maps: {},
    _routesFeatureCollection: null,

    createMap: async function (containerDivId, dotNetRef) {
        let settings = await dotNetRef.invokeMethodAsync('getMapSettings');

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
            _getCheckpointPulse().then(function (pulse) { pulse.ensureLayer(map); });

            // Pre-create the crossing-trail layer so the first crossing renders immediately.
            _getCheckpointTrail().then(function (trail) { trail.ensureLayer(map); });

            let containerDiv = document.getElementById(containerDivId);
            if (containerDiv) {
                console.debug('[ChefMap] map load complete, notifying Blazor (notifyMapReadyAsync) for ' + containerDivId);
                dotNetRef.invokeMethodAsync('notifyMapReadyAsync');
            } else {
                console.warn('[ChefMap] map load complete but container div not found: ' + containerDivId);
            }
        });
    },

    setMapZoom: function (containerDivId, zoom) {
        let map = ChefMap.maps[containerDivId];
        if (!map) return;
        map.setZoom(zoom);
    },

    setMapStyle: function (containerDivId, styleUrl) {
        let map = ChefMap.maps[containerDivId];
        if (!map) return Promise.resolve();

        // Snapshot current vehicles data before the style wipes everything.
        let vehiclesData = { type: 'FeatureCollection', features: [] };
        try {
            let vSrc = map.getSource('vehicles');
            if (vSrc) vehiclesData = vSrc._data || vehiclesData;
        } catch (e) { }

        // diff:false forces a FULL style reload. With MapLibre's default diff:true,
        // swapping between two schema-compatible MapTiler styles applies an incremental
        // diff that wipes our custom sources/layers but never fires 'style.load' — so the
        // restore handler below (and the awaited Promise) would never run, leaving routes,
        // vehicles, and checkpoints stripped after the swap. Full reload guarantees the event.
        map.setStyle(styleUrl, { diff: false });

        // Return a Promise that resolves after style.load so the C# caller can await it,
        // then re-render routes from its own cache (no re-fetch). A timed fallback guarantees
        // the C# await never deadlocks even if 'style.load' fails to fire (e.g. bad style URL).
        return new Promise(function (resolve) {
            let restored = false;
            let fallbackTimer = null;

            function restore() {
                if (restored) return;
                restored = true;
                if (fallbackTimer) clearTimeout(fallbackTimer);

                // Re-add the vehicles source and layer (empty shell; animator will update it).
                try {
                    if (!map.getSource('vehicles')) {
                        map.addSource('vehicles', { type: 'geojson', data: vehiclesData });
                    }
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
                } catch (e) {
                    console.warn('[ChefMap] setMapStyle: could not restore vehicles layer: ' + e);
                }

                // Re-add all-checkpoints dot layer (trigger-points-layer is wiped by setStyle).
                try {
                    if (!map.getSource('trigger-points')) {
                        let allFeatures = Object.values(ChefMap._triggerPointFeatures).flat();
                        map.addSource('trigger-points', { type: 'geojson', data: { type: 'FeatureCollection', features: allFeatures } });
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
                } catch (e) {
                    console.warn('[ChefMap] setMapStyle: could not restore trigger-points layer: ' + e);
                }

                // Re-add the consolidated routes source+layer from cached data.
                try {
                    if (ChefMap._routesFeatureCollection && !map.getSource('routes')) {
                        map.addSource('routes', { type: 'geojson', data: ChefMap._routesFeatureCollection });
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
                } catch (e) {
                    console.warn('[ChefMap] setMapStyle: could not restore routes layer: ' + e);
                }

                // Re-add pulse overlay and clear any in-flight pulses (FR-012).
                _getCheckpointPulse().then(function (pulse) {
                    pulse.ensureLayer(map);
                    pulse.reset(map);
                });

                // Re-add the crossing-trail layer so the next crossing renders after a basemap swap
                // (Principle VII). Active trails are sub-second and need not be preserved.
                try { _getCheckpointTrail().then(function (trail) { trail.ensureLayer(map); }); }
                catch (e) { console.warn('[ChefMap] setMapStyle: could not restore crossing-trail layer: ' + e); }

                // Signal C# to re-render routes from cache (addAllRoutes will hit setData branch).
                resolve({});
            }

            map.once('style.load', restore);
            // Safety net: if style.load never fires, restore anyway so routes still re-render.
            fallbackTimer = setTimeout(restore, 4000);
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

        let color = ChefMap._routeColorsByRouteJoinKey[routeJoinKey] || '#facc15';
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
        let color = ChefMap._routeColorsByRouteJoinKey[routeJoinKey] || '#facc15';   // FR-005

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

    addAllRoutes: function (containerDivId, routes) {
        let map = ChefMap.maps[containerDivId];
        if (!map) {
            console.warn('[ChefMap] addAllRoutes: no map for containerDivId=' + containerDivId);
            return;
        }

        let features = [];
        (routes || []).forEach(function (route) {
            if (!route.coordinates || route.coordinates.length === 0) return;

            let lineColor = route.color || '#6b7280';

            ChefMap._routeColorsByRouteJoinKey[route.routeJoinKey] = lineColor;

            ChefMapAnimator.loadRouteGeometry(route.routeJoinKey, route.coordinates);

            features.push({
                type: 'Feature',
                id: route.routeJoinKey,
                geometry: { type: 'LineString', coordinates: route.coordinates },
                properties: { routeJoinKey: route.routeJoinKey, color: lineColor }
            });
        });

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
