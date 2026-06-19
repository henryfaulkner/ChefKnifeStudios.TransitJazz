let _checkpointPulseModule = null;
async function _getCheckpointPulse() {
    if (!_checkpointPulseModule) {
        _checkpointPulseModule = await import('/_content/ChefKnifeStudios.MartaJazz.Client.Shared/js/checkpoint-pulse.js');
    }
    return _checkpointPulseModule;
}

window.ChefMap = {
    maps: {},

    createMap: async function (containerDivId, dotNetRef) {
        let settings = await dotNetRef.invokeMethodAsync('getMapSettings');

        let map = new maplibregl.Map({
            container: containerDivId,
            style: settings.styleUrl,
            center: settings.center,
            zoom: settings.zoom,
            minZoom: 7,
            maxZoom: 18,
            dragRotate: false,
            touchZoomRotate: false
        });

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
                    'circle-radius': 6,
                    'circle-color': '#22c55e',
                    'circle-stroke-width': 1,
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

    toggleTraffic: function (containerDivId, on) {
        console.info('[ChefMap] toggleTraffic: traffic layer not implemented for POC (no-op)');
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

        map.setStyle(styleUrl);

        // Return a Promise that resolves after style.load so the C# caller can await it,
        // then re-render routes from its own cache (no re-fetch).
        return new Promise(function (resolve) {
            map.once('style.load', function () {
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
                                'circle-radius': 6,
                                'circle-color': '#22c55e',
                                'circle-stroke-width': 1,
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
                        }, 'vehicles-layer');
                    }
                } catch (e) {
                    console.warn('[ChefMap] setMapStyle: could not restore trigger-points layer: ' + e);
                }

                // Re-add pulse overlay and clear any in-flight pulses (FR-012).
                _getCheckpointPulse().then(function (pulse) {
                    pulse.ensureLayer(map);
                    pulse.reset(map);
                });

                // Signal C# to re-render routes from cache; colors will reapply via debounce in addRouteShapeFeature.
                resolve({});
            });
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
        let map = ChefMap.maps[containerDivId];
        if (!map) return;

        let routeColor = ChefMap._routeColorsByRouteId[routeId] || '#facc15';

        ChefMap._triggerPointFeatures[routeId] = triggerPoints.map(function (tp) {
            let coord = coords[tp.index] || coords[coords.length - 1];
            return {
                type: 'Feature',
                geometry: { type: 'Point', coordinates: coord },
                properties: { routeId: routeId, triggerIndex: tp.index, alongDistanceM: tp.alongDistanceM, color: routeColor }
            };
        });

        let allFeatures = Object.values(ChefMap._triggerPointFeatures).flat();
        let fc = { type: 'FeatureCollection', features: allFeatures };

        let source = map.getSource('trigger-points');
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
            }, 'vehicles-layer');
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

    _routeColors: {},      // routeLayerId → data color, set at addRouteShapeFeature time
    _routeColorsByRouteId: {},  // routeId → data color, for vehicle dot coloring
    _applyRouteColorsTimer: null,

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
        if (!map) return;
        let style = map.getStyle();
        if (!style) return;

        let selected = new Set(routeIds || []);
        (style.layers || []).forEach(function (layer) {
            if (!layer.id || !layer.id.startsWith('route-layer-')) return;
            let id = layer.id;
            if (!map.getLayer(id)) return;
            let routeId = id.substring('route-layer-'.length);
            if (selected.has(routeId)) {
                map.setPaintProperty(id, 'line-opacity', 0.95);
                map.setPaintProperty(id, 'line-color', ChefMap._routeColors[id] || '#22c55e');
            } else {
                map.setPaintProperty(id, 'line-opacity', 0.3);
                map.setPaintProperty(id, 'line-color', '#d1d5db');
            }
        });
    },

    focusRoute: function (containerDivId, routeId) {
        let map = ChefMap.maps[containerDivId];
        if (!map) return;

        let style = map.getStyle();
        if (!style) return;

        let targetLayerId = 'route-layer-' + routeId;

        (style.layers || []).forEach(function (layer) {
            if (!layer.id || !layer.id.startsWith('route-layer-')) return;
            let id = layer.id;
            if (!map.getLayer(id)) return;

            if (id === targetLayerId) {
                map.setPaintProperty(id, 'line-opacity', 0.95);
                map.setPaintProperty(id, 'line-color', ChefMap._routeColors[id] || '#22c55e');
            } else {
                map.setPaintProperty(id, 'line-opacity', 0.3);
                map.setPaintProperty(id, 'line-color', '#d1d5db');
            }
        });
    },

    clearRouteFocus: function (containerDivId) {
        let map = ChefMap.maps[containerDivId];
        if (!map) return;

        let style = map.getStyle();
        if (!style) return;

        (style.layers || []).forEach(function (layer) {
            if (!layer.id || !layer.id.startsWith('route-layer-')) return;
            let id = layer.id;
            if (!map.getLayer(id)) return;
            map.setPaintProperty(id, 'line-opacity', 0.7);
            map.setPaintProperty(id, 'line-color', '#6b7280');
        });
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

    setVehiclesVisible: function (containerDivId, visible) {
        let map = ChefMap.maps[containerDivId];
        if (!map) return;
        if (!map.getLayer('vehicles-layer')) return;
        map.setLayoutProperty('vehicles-layer', 'visibility', visible ? 'visible' : 'none');
    },

    addRouteShapeFeature: function (containerDivId, routeId, coordinates, color) {
        let map = ChefMap.maps[containerDivId];
        if (!map) {
            console.warn('[ChefMap] addRouteShapeFeature: no map for containerDivId=' + containerDivId);
            return;
        }

        let sourceId = 'route-' + routeId;
        let layerId = 'route-layer-' + routeId;
        let lineColor = color || '#0078D4';

        console.debug('[ChefMap] addRouteShapeFeature: routeId=' + routeId
            + ' coords=' + (coordinates ? coordinates.length : 'null')
            + ' color=' + lineColor
            + ' sourceExists=' + !!map.getSource(sourceId));

        if (!coordinates || coordinates.length === 0) {
            console.warn('[ChefMap] addRouteShapeFeature: skipping routeId=' + routeId + ' — coordinates null/empty');
            return;
        }

        let geojson = {
            type: 'Feature',
            geometry: { type: 'LineString', coordinates: coordinates },
            properties: { routeId: routeId, color: lineColor }
        };

        // Store colors for route line focus/unfocus and vehicle dot coloring.
        ChefMap._routeColors[layerId] = lineColor;
        ChefMap._routeColorsByRouteId[routeId] = lineColor;

        // Debounce the vehicle color update — routes load one at a time, apply once after they settle.
        clearTimeout(ChefMap._applyRouteColorsTimer);
        ChefMap._applyRouteColorsTimer = setTimeout(function () {
            ChefMap._applyVehicleRouteColors(containerDivId);
        }, 200);

        let source = map.getSource(sourceId);
        if (source) {
            console.debug('[ChefMap] addRouteShapeFeature: updating existing source for routeId=' + routeId);
            source.setData(geojson);
        } else {
            console.debug('[ChefMap] addRouteShapeFeature: adding new source+layer for routeId=' + routeId);
            map.addSource(sourceId, { type: 'geojson', data: geojson });
            map.addLayer({
                id: layerId,
                type: 'line',
                source: sourceId,
                layout: { 'line-join': 'round', 'line-cap': 'round' },
                paint: { 'line-color': '#6b7280', 'line-width': 4, 'line-opacity': 0.7 }
            }, 'vehicles-layer');
            console.debug('[ChefMap] addRouteShapeFeature: layer added for routeId=' + routeId);
        }
    }
};
