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
                    'circle-radius': ['match', ['get', 'transitMode'], 'Rail', 9, 6],
                    'circle-color': '#22c55e',
                    'circle-stroke-width': ['match', ['get', 'transitMode'], 'Rail', 2, 1],
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
                                'circle-radius': ['match', ['get', 'transitMode'], 'Rail', 9, 6],
                                'circle-color': '#22c55e',
                                'circle-stroke-width': ['match', ['get', 'transitMode'], 'Rail', 2, 1],
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

    // Coordinate lookup for pulse targeting — routeId → Feature[].
    // Also pushed to the 'trigger-points' source for the all-checkpoints overlay.
    _triggerPointFeatures: {},

    addTriggerPointMarkers: function (containerDivId, routeId, triggerPoints, coords) {
        var map = ChefMap.maps[containerDivId];
        if (!map) return;

        var routeColor = ChefMap._routeColorsByRouteId[routeId] || '#facc15';

        // Accumulate this route's features — do NOT rebuild/flush the combined collection here.
        // flushTriggerPoints() does a single combined setData after all routes are added.
        ChefMap._triggerPointFeatures[routeId] = triggerPoints.map(function (tp) {
            var coord = coords[tp.index] || coords[coords.length - 1];
            return {
                type: 'Feature',
                geometry: { type: 'Point', coordinates: coord },
                properties: { routeId: routeId, triggerIndex: tp.index, alongDistanceM: tp.alongDistanceM, color: routeColor }
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

    _routeColorsByRouteId: {},  // routeId → data color, for vehicle dot coloring

    _applyVehicleRouteColors: function (containerDivId) {
        let map = ChefMap.maps[containerDivId];
        if (!map || !map.getLayer('vehicles-layer')) return;

        let entries = Object.entries(ChefMap._routeColorsByRouteId);
        if (entries.length === 0) return;

        // Build a MapLibre match expression: ['match', ['get', 'routeId'], id, color, ..., fallback]
        let matchExpr = ['match', ['get', 'routeId']];
        entries.forEach(function (pair) {
            matchExpr.push(pair[0], pair[1]);
        });
        matchExpr.push('#6b7280');  // fallback for unknown routeId

        map.setPaintProperty('vehicles-layer', 'circle-color', matchExpr);
    },

    focusRoutes: function (containerDivId, routeIds) {
        let map = ChefMap.maps[containerDivId];
        if (!map || !map.getLayer('routes-layer')) return;

        let selected = new Set(routeIds || []);

        // Build match expressions: selected routes get their route color at full opacity;
        // all others get grey at reduced opacity.
        let colorExpr = ['match', ['get', 'routeId']];
        let opacityExpr = ['match', ['get', 'routeId']];
        Object.entries(ChefMap._routeColorsByRouteId).forEach(function ([rid, color]) {
            colorExpr.push(rid, selected.has(rid) ? color : '#9ca3af');
            opacityExpr.push(rid, selected.has(rid) ? 0.95 : 0.35);
        });
        colorExpr.push('#9ca3af');   // fallback
        opacityExpr.push(0.35);      // fallback

        map.setPaintProperty('routes-layer', 'line-color', colorExpr);
        map.setPaintProperty('routes-layer', 'line-opacity', opacityExpr);
    },

    focusRoute: function (containerDivId, routeId) {
        ChefMap.focusRoutes(containerDivId, [routeId]);
    },

    clearRouteFocus: function (containerDivId) {
        let map = ChefMap.maps[containerDivId];
        if (!map || !map.getLayer('routes-layer')) return;

        // No selection — all routes grey at default opacity.
        map.setPaintProperty('routes-layer', 'line-color', '#6b7280');
        map.setPaintProperty('routes-layer', 'line-opacity', 0.7);
    },

    pulseCheckpoint: async function (containerDivId, routeId, triggerIndex) {
        let map = ChefMap.maps[containerDivId];
        if (!map) return;

        let features = ChefMap._triggerPointFeatures[routeId];
        if (!features) {
            console.warn('[ChefMap] pulseCheckpoint: no trigger features for routeId=' + routeId);
            return;
        }
        let feature = features.find(function (f) { return f.properties.triggerIndex === triggerIndex; });
        if (!feature) {
            console.warn('[ChefMap] pulseCheckpoint: triggerIndex ' + triggerIndex + ' not found for routeId=' + routeId);
            return;
        }

        let color = ChefMap._routeColorsByRouteId[routeId] || '#facc15';
        let coords = feature.geometry.coordinates;

        try {
            let pulse = await _getCheckpointPulse();
            pulse.ensureLayer(map);
            pulse.start(map, routeId, triggerIndex, coords, color);
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

    startCrossingTrail: async function (containerDivId, routeId, vehicleId, triggerIndex, durationSec) {
        let map = ChefMap.maps[containerDivId];
        if (!map) return;

        // Anchor: reuse the trigger feature (same lookup as pulseCheckpoint).
        let features = ChefMap._triggerPointFeatures[routeId];
        if (!features) return;                       // route geometry not loaded yet
        let feature = features.find(function (f) { return f.properties.triggerIndex === triggerIndex; });
        if (!feature) return;

        let anchorCoord = feature.geometry.coordinates;
        let anchorDistanceM = feature.properties.alongDistanceM;
        let color = ChefMap._routeColorsByRouteId[routeId] || '#facc15';   // FR-005

        // Speed: read empirical speed from the animator (audio-independent — R5).
        let vstate = ChefMapAnimator.vehicles[vehicleId];
        let speedMps = (vstate && (vstate.empiricalSpeed ?? vstate.speed)) || 0;

        try {
            let trail = await _getCheckpointTrail();
            trail.ensureLayer(map);
            trail.start(map, routeId, vehicleId, triggerIndex, anchorCoord, anchorDistanceM, color, speedMps, durationSec);
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

            ChefMap._routeColorsByRouteId[route.routeId] = lineColor;

            ChefMapAnimator.loadRouteGeometry(route.routeId, route.coordinates);

            features.push({
                type: 'Feature',
                id: route.routeId,
                geometry: { type: 'LineString', coordinates: route.coordinates },
                properties: { routeId: route.routeId, color: lineColor }
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
