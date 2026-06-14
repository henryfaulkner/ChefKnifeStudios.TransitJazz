"""
Neighborhood Routes Generator
==============================
Spatially joins Atlanta neighborhood polygons (City-of-Atlanta GeoJSON) against
MARTA bus route LineStrings (MartaJazz API /gtfs/routes/shapes) and emits two
committed JSON files:

  neighborhood_routes.json       -- lean mapping (skills, quick Q&A)
  neighborhood_routes_full.json  -- full demographic dump (deep-dive, per-objectId)

Run manually. Re-generate when:
  - MARTA GTFS route shapes change (new routes, modified shapes)
  - The City of Atlanta updates the neighborhood GeoJSON source

Do NOT schedule this script; it calls a live API and writes committed artifacts.
"""

import argparse
import json
import os
import sys
from datetime import datetime, timezone


def parse_args():
    parser = argparse.ArgumentParser(
        description="Spatially join Atlanta neighborhoods to MARTA bus routes."
    )
    default_geojson = os.path.expanduser(
        "~/Downloads/Official_Neighborhoods_with_Current_Demographic_Data_(2024).geojson"
    )
    default_api = (
        "https://marta-jazz-dev-ca-server.jollytree-dd5ca774.eastus2.azurecontainerapps.io"
    )
    default_out_dir = os.path.dirname(os.path.abspath(__file__))

    parser.add_argument("--geojson", default=default_geojson, help="Path to source neighborhood GeoJSON")
    parser.add_argument("--api", default=default_api, help="MJ API base URL (no trailing slash)")
    parser.add_argument("--out-dir", default=default_out_dir, help="Directory to write output JSON files")
    return parser.parse_args()


class Neighborhood:
    __slots__ = (
        "object_id", "name", "npu", "sq_miles", "population",
        "median_household_income", "transit_commute_percent",
        "car_alone_percent", "work_from_home_percent",
        "all_properties", "geometry", "matched_routes",
    )

    def __init__(self, object_id, name, npu, sq_miles, population,
                 median_household_income, transit_commute_percent,
                 car_alone_percent, work_from_home_percent,
                 all_properties, geometry):
        self.object_id = object_id
        self.name = name
        self.npu = npu
        self.sq_miles = sq_miles
        self.population = population
        self.median_household_income = median_household_income
        self.transit_commute_percent = transit_commute_percent
        self.car_alone_percent = car_alone_percent
        self.work_from_home_percent = work_from_home_percent
        self.all_properties = all_properties
        self.geometry = geometry
        self.matched_routes = []


class Route:
    __slots__ = ("route_id", "route_short_name", "linestring")

    def __init__(self, route_id, route_short_name, linestring):
        self.route_id = route_id
        self.route_short_name = route_short_name
        self.linestring = linestring


def _opt_float(props, key):
    v = props.get(key)
    if v is None:
        return None
    try:
        return float(v)
    except (TypeError, ValueError):
        return None


def _opt_int(props, key):
    v = props.get(key)
    if v is None:
        return None
    try:
        return int(round(float(v)))
    except (TypeError, ValueError):
        return None


def load_neighborhoods(geojson_path):
    from shapely.geometry import shape

    try:
        with open(geojson_path, encoding="utf-8") as f:
            data = json.load(f)
    except FileNotFoundError:
        raise RuntimeError(f"GeoJSON not found: {geojson_path}")
    except json.JSONDecodeError as exc:
        raise RuntimeError(f"GeoJSON parse error: {exc}")

    features = data.get("features", [])
    neighborhoods = []
    skipped = 0

    for feat in features:
        props = feat.get("properties") or {}
        object_id = props.get("OBJECTID_1")
        name = props.get("NAME")

        if object_id is None or name is None:
            skipped += 1
            continue

        try:
            geom = shape(feat["geometry"])
        except Exception:
            skipped += 1
            continue

        n = Neighborhood(
            object_id=int(object_id),
            name=str(name),
            npu=props.get("NPU"),
            sq_miles=_opt_float(props, "SQMILES"),
            population=_opt_int(props, "population"),
            median_household_income=_opt_int(props, "householdi"),
            transit_commute_percent=_opt_float(props, "commute__3"),
            car_alone_percent=_opt_float(props, "commute__1"),
            work_from_home_percent=_opt_float(props, "commute__5"),
            all_properties=dict(props),
            geometry=geom,
        )
        neighborhoods.append(n)

    return neighborhoods, skipped


def fetch_routes(api_base):
    import requests
    from shapely.geometry import LineString

    api_base = api_base.rstrip("/")
    url = f"{api_base}/gtfs/routes/shapes"

    try:
        resp = requests.get(url, timeout=30)
        resp.raise_for_status()
    except requests.exceptions.ConnectionError as exc:
        raise RuntimeError(f"Cannot connect to API at {url}: {exc}")
    except requests.exceptions.Timeout:
        raise RuntimeError(f"API request timed out: {url}")
    except requests.exceptions.HTTPError as exc:
        raise RuntimeError(f"API returned error {resp.status_code}: {exc}")

    try:
        data = resp.json()
    except ValueError as exc:
        raise RuntimeError(f"API response is not valid JSON: {exc}")

    if not isinstance(data, list):
        raise RuntimeError(
            f"API response top level is not a bare array (got {type(data).__name__}). "
            "Endpoint shape may have changed — expected [{...}, ...] directly."
        )

    if len(data) == 0:
        raise RuntimeError(
            "API returned an empty array — no routes loaded. "
            "An empty result would produce a misleading zero-match output; aborting."
        )

    routes = []
    skipped = 0

    for feat in data:
        try:
            coords = feat["geometry"]["coordinates"]
            if not coords:
                skipped += 1
                continue
            linestring = LineString(coords)
            route_id = str(feat["properties"]["routeId"])
            route_short_name = str(feat["properties"]["routeShortName"])
        except (KeyError, TypeError, ValueError):
            skipped += 1
            continue

        routes.append(Route(route_id=route_id, route_short_name=route_short_name, linestring=linestring))

    return routes, skipped


def spatial_join(neighborhoods, routes):
    for n in neighborhoods:
        n.matched_routes = [r for r in routes if n.geometry.intersects(r.linestring)]


def _round_1dp(v):
    if v is None:
        return None
    return round(float(v), 1)


def _round_int(v):
    if v is None:
        return None
    return int(round(float(v)))


def build_lean_entry(neighborhood):
    return {
        "objectId": neighborhood.object_id,
        "name": neighborhood.name,
        "npu": neighborhood.npu,
        "sqMiles": neighborhood.sq_miles,
        "population": _round_int(neighborhood.population),
        "medianHouseholdIncome": _round_int(neighborhood.median_household_income),
        "transitCommutePercent": _round_1dp(neighborhood.transit_commute_percent),
        "carAlonePercent": _round_1dp(neighborhood.car_alone_percent),
        "workFromHomePercent": _round_1dp(neighborhood.work_from_home_percent),
        "routes": [
            {"routeId": r.route_id, "routeShortName": r.route_short_name}
            for r in neighborhood.matched_routes
        ],
    }


def write_lean(out_dir, neighborhoods, source_geojson_path):
    entries = sorted(
        [build_lean_entry(n) for n in neighborhoods],
        key=lambda e: e["name"],
    )
    payload = {
        "generatedAt": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "sourceGeoJson": os.path.basename(source_geojson_path),
        "neighborhoods": entries,
    }
    out_path = os.path.join(out_dir, "neighborhood_routes.json")
    with open(out_path, "w", encoding="utf-8") as f:
        json.dump(payload, f, indent=2, ensure_ascii=False)
    return out_path


def write_full(out_dir, neighborhoods, source_geojson_path):
    records = {}
    for n in neighborhoods:
        records[str(n.object_id)] = n.all_properties
    payload = {
        "generatedAt": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "sourceGeoJson": os.path.basename(source_geojson_path),
        "neighborhoods": records,
    }
    out_path = os.path.join(out_dir, "neighborhood_routes_full.json")
    with open(out_path, "w", encoding="utf-8") as f:
        json.dump(payload, f, ensure_ascii=False)
    return out_path


def print_summary(neighborhoods, skipped_neighborhoods, skipped_routes):
    with_routes = [n for n in neighborhoods if n.matched_routes]
    without_routes = [n for n in neighborhoods if not n.matched_routes]

    unique_route_ids = {r.route_id for n in neighborhoods for r in n.matched_routes}
    zero_names = ", ".join(sorted(n.name for n in without_routes))

    print(f"Total neighborhoods processed: {len(neighborhoods)}")
    print(f"Neighborhoods with >=1 route: {len(with_routes)}")
    print(f"Neighborhoods with 0 routes ({len(without_routes)}): {zero_names}")
    print(f"Total unique routes matched: {len(unique_route_ids)}")
    if skipped_neighborhoods > 0:
        print(f"Skipped neighborhood features (missing key/bad geometry): {skipped_neighborhoods}")
    if skipped_routes > 0:
        print(f"Skipped route features (bad geometry): {skipped_routes}")


def main():
    args = parse_args()
    api_base = args.api.rstrip("/")
    geojson_path = os.path.expanduser(args.geojson)
    out_dir = args.out_dir

    try:
        neighborhoods, skipped_neighborhoods = load_neighborhoods(geojson_path)
        routes, skipped_routes = fetch_routes(api_base)
    except RuntimeError as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        sys.exit(1)

    spatial_join(neighborhoods, routes)

    try:
        lean_path = write_lean(out_dir, neighborhoods, geojson_path)
        full_path = write_full(out_dir, neighborhoods, geojson_path)
    except OSError as exc:
        print(f"ERROR writing output: {exc}", file=sys.stderr)
        sys.exit(1)

    print_summary(neighborhoods, skipped_neighborhoods, skipped_routes)
    print(f"\nWrote: {lean_path}")
    print(f"Wrote: {full_path}")


if __name__ == "__main__":
    main()
