window.ChefMap = {
    maps: {},

    createMap: async function (containerDivId, dotNetRef) {
        let settings = await dotNetRef.invokeMethodAsync('getMapSettings');

        let map = new maplibregl.Map({
            container: containerDivId,
            style: settings.styleUrl,
            center: settings.center,
            zoom: settings.zoom
        });

        ChefMap.maps[containerDivId] = map;

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

    setMapStyle: function (containerDivId, styleName) {
        console.info('[ChefMap] setMapStyle: style switching not implemented for POC (no-op)');
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

    // Debug: render trigger-point dots for all configured routes.
    // Accumulates points across calls (one call per route); idempotent per routeId.
    _triggerPointFeatures: {},  // routeId → Feature[]

    addTriggerPointMarkers: function (containerDivId, routeId, triggerPoints, coords) {
        let map = ChefMap.maps[containerDivId];
        if (!map) return;

        // Build one Point feature per trigger point using the route's coord array
        ChefMap._triggerPointFeatures[routeId] = triggerPoints.map(function (tp) {
            let coord = coords[tp.index] || coords[coords.length - 1];
            return {
                type: 'Feature',
                geometry: { type: 'Point', coordinates: coord },
                properties: { routeId: routeId, triggerIndex: tp.index, alongDistanceM: tp.alongDistanceM }
            };
        });

        // Flatten all routes into one FeatureCollection and push to the shared source
        let allFeatures = Object.values(ChefMap._triggerPointFeatures).flat();
        let fc = { type: 'FeatureCollection', features: allFeatures };

        let source = map.getSource('trigger-points');
        if (!source) {
            map.addSource('trigger-points', { type: 'geojson', data: fc });
            map.addLayer({
                id: 'trigger-points-layer',
                type: 'circle',
                source: 'trigger-points',
                layout: { 'visibility': 'none' },
                paint: {
                    'circle-radius': 4,
                    'circle-color': '#facc15',       // yellow — visible against route lines
                    'circle-opacity': 0.85,
                    'circle-stroke-width': 1,
                    'circle-stroke-color': '#78350f'  // dark amber outline
                }
            }, 'vehicles-layer');  // insert below vehicles so buses render on top
        } else {
            source.setData(fc);
        }
    },

    _preFocusColors: {},

    focusRoute: function (containerDivId, routeId) {
        let map = ChefMap.maps[containerDivId];
        if (!map) return;

        let style = map.getStyle();
        if (!style) return;

        let targetLayerId = 'route-layer-' + routeId;

        (style.layers || []).forEach(function (layer) {
            if (!layer.id || !layer.id.startsWith('route-layer-')) return;
            let id = layer.id;

            // Stash original color on first focus pass
            if (ChefMap._preFocusColors[id] === undefined) {
                ChefMap._preFocusColors[id] = map.getPaintProperty(id, 'line-color');
            }

            if (!map.getLayer(id)) return;

            if (id === targetLayerId) {
                map.setPaintProperty(id, 'line-opacity', 0.95);
                map.setPaintProperty(id, 'line-color', ChefMap._preFocusColors[id]);
            } else {
                map.setPaintProperty(id, 'line-opacity', 0.15);
                map.setPaintProperty(id, 'line-color', '#374151');
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
            map.setPaintProperty(id, 'line-opacity', 0.85);
            map.setPaintProperty(id, 'line-color', ChefMap._preFocusColors[id] !== undefined ? ChefMap._preFocusColors[id] : map.getPaintProperty(id, 'line-color'));
        });

        ChefMap._preFocusColors = {};
    },

    setCheckpointVisibility: function (containerDivId, visible) {
        let map = ChefMap.maps[containerDivId];
        if (!map) return;
        if (!map.getLayer('trigger-points-layer')) return;
        map.setLayoutProperty('trigger-points-layer', 'visibility', visible ? 'visible' : 'none');
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
                paint: { 'line-color': lineColor, 'line-width': 4, 'line-opacity': 0.85 }
            }, 'vehicles-layer');
            console.debug('[ChefMap] addRouteShapeFeature: layer added for routeId=' + routeId);
        }
    }
};
