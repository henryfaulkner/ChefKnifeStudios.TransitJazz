#!/usr/bin/env python3
"""Generate the City Score, transit mix, and route insights prototype.

Uses verified category hours. Writes local JSON; publishing is a separate action.
"""

import argparse
import json
import re
from pathlib import Path
from route_insights import add_routes, catalog_markers

ROOT = Path(__file__).resolve().parents[2]
CONTRACTS = ROOT / "specs/056-city-transit-type-insights/contracts"
OUTPUT = Path(__file__).resolve().parent / "dashboards/transitjazz-city-category-insights-prototype.json"
VERSION = "observed-city-category-statistics-v1"
UID = "transitjazz-category-insights-lab"
PG_TYPE = "grafana-postgresql-datasource"
FROM = "($__timeFrom()::timestamptz)"
TO = "($__timeTo()::timestamptz)"
CATEGORIES = "SELECT DISTINCT unnest(ARRAY[${category:sqlstring}]::text[]) AS selected_category"


def recipe(name):
    """Bind contract parameters as quoted Grafana variables or fixed SQL."""
    replacements = {
        "city_slug": "${city:sqlstring}",
        "category": "selected.selected_category",
        "definition_version": f"'{VERSION}'",
        "from_utc": FROM,
        "to_utc": TO,
    }
    sql = (CONTRACTS / name).read_text(encoding="utf-8").strip().removesuffix(";")
    return re.sub(r"(?<!:):([a-z_]+)\b", lambda m: replacements[m[1]], sql)


def reports(name):
    return f"""WITH selected_categories AS ({CATEGORIES}),
reports AS (
    SELECT report.* FROM selected_categories selected
    CROSS JOIN LATERAL (
{recipe(name)}
    ) report
)
"""


def discovery(column, city_filter=""):
    return f"""SELECT DISTINCT {column} AS __text, {column} AS __value FROM (
    SELECT {column} FROM public.city_category_minute_statistics
    WHERE stat_minute_utc >= {FROM} AND stat_minute_utc < {TO}
      AND definition_version = '{VERSION}' {city_filter}
    UNION
    SELECT {column} FROM public.city_category_hour_statistics
    WHERE hour_start_utc < {TO} AND hour_start_utc + interval '1 hour' > {FROM}
      AND definition_version = '{VERSION}' {city_filter}
) retained ORDER BY __text LIMIT 100;"""


def observations():
    return reports("hourly-insights.sql") + f""", matched_hours AS (
    SELECT hour_start_utc, healthy_cadence_limit_seconds FROM reports
    GROUP BY hour_start_utc, healthy_cadence_limit_seconds
    HAVING count(DISTINCT category) = (SELECT count(*) FROM selected_categories)
), comparable AS (
    SELECT r.* FROM reports r JOIN matched_hours USING (hour_start_utc, healthy_cadence_limit_seconds)
), policies AS (
    SELECT DISTINCT healthy_cadence_limit_seconds FROM reports
    UNION ALL SELECT NULL::integer WHERE NOT EXISTS (SELECT 1 FROM reports)
), hour_keys AS (
    SELECT hour_start_utc FROM generate_series(date_trunc('hour', {FROM}, 'UTC'),
        date_trunc('hour', {TO} - interval '1 microsecond', 'UTC'),
        interval '1 hour') AS keys(hour_start_utc)
    WHERE hour_start_utc >= {FROM} AND hour_start_utc + interval '1 hour' <= {TO}
), named AS (
    SELECT selected_category AS category, healthy_cadence_limit_seconds,
        initcap(replace(selected_category, '-', ' ')) ||
        CASE WHEN (SELECT count(*) FROM policies) > 1 THEN
            ' (' || healthy_cadence_limit_seconds::text || 's policy)' ELSE '' END AS name
    FROM selected_categories CROSS JOIN policies
), period AS (
    SELECT category, healthy_cadence_limit_seconds,
        sum(distance_meters_sum) AS recorded_meters
    FROM comparable GROUP BY category, healthy_cadence_limit_seconds
)
"""


def timeline(field, percentage=False):
    if percentage:
        return observations() + """, shares AS (
    SELECT r.*, sum(distance_meters_sum) OVER (
        PARTITION BY hour_start_utc, healthy_cadence_limit_seconds) AS total FROM comparable r
)
SELECT k.hour_start_utc AS time,
    (100 * r.distance_meters_sum / nullif(r.total, 0))::double precision AS value, n.name AS metric
FROM hour_keys k CROSS JOIN named n
LEFT JOIN shares r ON r.hour_start_utc = k.hour_start_utc AND r.category = n.category
    AND r.healthy_cadence_limit_seconds = n.healthy_cadence_limit_seconds
ORDER BY time, metric;"""
    value = "r.distance_meters_sum / 1000" if field == "distance_meters_sum" else f"r.{field}"
    return observations() + f"""SELECT k.hour_start_utc AS time,
    ({value})::double precision AS value, n.name AS metric
FROM hour_keys k CROSS JOIN named n
LEFT JOIN comparable r ON r.hour_start_utc = k.hour_start_utc AND r.category = n.category
    AND r.healthy_cadence_limit_seconds = n.healthy_cadence_limit_seconds
ORDER BY time, metric;"""


def cumulative_distance():
    return observations() + """, accumulated AS (
    SELECT r.*, sum(distance_meters_sum) OVER (
        PARTITION BY category, healthy_cadence_limit_seconds
        ORDER BY hour_start_utc ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) / 1000 AS recorded_km
    FROM comparable r
)
SELECT k.hour_start_utc AS time, r.recorded_km::double precision AS value, n.name AS metric
FROM hour_keys k CROSS JOIN named n
LEFT JOIN accumulated r ON r.hour_start_utc = k.hour_start_utc AND r.category = n.category
    AND r.healthy_cadence_limit_seconds = n.healthy_cadence_limit_seconds
ORDER BY time, metric;"""


def headline(kind):
    if kind == "distance":
        return observations() + """SELECT CASE WHEN (SELECT count(*) FROM policies) > 1
    THEN 'Multiple collection policies' ELSE coalesce(
        to_char(sum(distance_meters_sum) / 1000, 'FM999,999,999,990') || ' km',
        'No comparable hours') END AS insight FROM comparable;"""
    if kind == "peak":
        return observations() + """, ranked AS (
    SELECT hour_start_utc, healthy_cadence_limit_seconds, sum(distance_meters_sum) AS distance
    FROM comparable GROUP BY hour_start_utc, healthy_cadence_limit_seconds
    ORDER BY distance DESC, hour_start_utc LIMIT 1
)
SELECT CASE WHEN (SELECT count(*) FROM policies) > 1 THEN 'Multiple collection policies'
    ELSE coalesce((SELECT to_char(hour_start_utc AT TIME ZONE 'UTC', 'DD Mon · HH24:MI')
        || ' UTC' FROM ranked), 'No comparable hours') END AS insight;"""
    if kind == "lead":
        return observations() + """, shares AS (
    SELECT n.name, p.recorded_meters / nullif(sum(p.recorded_meters) OVER (
        PARTITION BY p.healthy_cadence_limit_seconds), 0) AS share
    FROM period p JOIN named n USING (category, healthy_cadence_limit_seconds)
    ORDER BY share DESC NULLS LAST, n.name LIMIT 1
)
SELECT CASE WHEN (SELECT count(*) FROM policies) > 1 THEN 'Multiple collection policies'
    ELSE coalesce((SELECT name || ' · ' || round(share * 100)::text || '%' FROM shares
        WHERE share IS NOT NULL), 'No recorded movement') END AS insight;"""
    return observations() + """SELECT count(DISTINCT hour_start_utc)::text || ' / ' ||
    (SELECT count(*) FROM hour_keys)::text || ' hours' AS insight FROM matched_hours;"""


def ensemble_sql():
    return observations() + """SELECT n.name AS category, (p.recorded_meters / 1000)::double precision AS value
FROM period p JOIN named n USING (category, healthy_cadence_limit_seconds)
WHERE p.recorded_meters > 0 AND (SELECT count(*) FROM policies) = 1 ORDER BY n.name;"""


def scatter_sql():
    return observations() + """SELECT r.hour_start_utc AS hour_utc, n.name AS transit_type,
    (r.avg_meters_per_observed_vehicle_hour / 1000)::double precision AS movement,
    r.published_crossings_per_hour::double precision AS opportunities,
    r.avg_active_vehicles::double precision AS vehicles,
    r.distinct_active_vehicle_count AS distinct_vehicles,
    r.distance_interval_count AS accepted_updates
FROM comparable r JOIN named n USING (category, healthy_cadence_limit_seconds)
WHERE r.distance_interval_count > 0 AND r.distinct_active_vehicle_count > 0
ORDER BY n.name, r.hour_start_utc;"""


def colors():
    return [{"matcher": {"id": "byRegexp", "options": f"^{name}(?: .*|$)"},
             "properties": [{"id": "color", "value": {"mode": "fixed", "fixedColor": color}}]}
            for name, color in {"Bus": "green", "Rail": "blue", "Streetcar": "purple", "Unknown": "orange"}.items()]


def generate(datasource_uid, schema_unavailable=False, route_history_available=False, default_routes=()):
    enabled = bool(datasource_uid) and not schema_unavailable
    ds = {"type": PG_TYPE, "uid": "${insights_source}"}
    panels = []

    def panel(kind, title, description, position, sql=None, unit="short"):
        item = {"id": len(panels) + 1, "type": kind, "title": title, "description": description,
                "gridPos": dict(zip(("x", "y", "w", "h"), position))}
        if sql:
            source = ds if enabled else {"type": "grafana", "uid": "-- Grafana --"}
            item.update({"datasource": source, "targets": [{
                "refId": "A", "datasource": source, "editorMode": "code",
                "format": "time_series" if kind == "timeseries" else "table",
                "rawQuery": True, "rawSql": sql, "hide": not enabled}],
                "fieldConfig": {"defaults": {"unit": unit, "decimals": 0, "min": 0,
                    "noValue": "No comparable observations", "color": {"mode": "palette-classic"}},
                    "overrides": colors()}})
        if kind == "timeseries":
            item["interval"] = "1h"
            item["fieldConfig"]["defaults"]["custom"] = {
                "drawStyle": "line", "lineInterpolation": "linear", "lineWidth": 2,
                "fillOpacity": 40, "showPoints": "auto", "spanNulls": False,
                "stacking": {"mode": "normal", "group": "A"}, "axisLabel": "Observed vehicles"}
            item["options"] = {
                "legend": {"displayMode": "list", "placement": "bottom", "showLegend": True, "calcs": []},
                "tooltip": {"mode": "multi", "sort": "desc", "hideZeros": False}}
        elif kind == "stat":
            item["fieldConfig"]["defaults"]["color"] = {"mode": "fixed", "fixedColor": "text"}
            item["options"] = {"reduceOptions": {"calcs": ["lastNotNull"], "fields": "/.*/", "values": False},
                "orientation": "horizontal", "textMode": "value", "colorMode": "none",
                "graphMode": "none", "justifyMode": "center", "wideLayout": True}
        if sql and not enabled:
            item["type"] = "text"
            item["options"] = {"mode": "markdown", "content": "Observations unavailable. " + description}
        panels.append(item)
        return item

    intro = panel("text", "Listen to the city. See its rhythm.", "Explore the selected city's transit ensemble.", (0, 0, 24, 3))
    intro["options"] = {"mode": "markdown", "content":
        "**${city:text}** · Follow the distance recorded by the transit ensemble, discover which cohorts carry the movement, and see how that movement relates to music.\n\n"
        "Distance comes from accepted movement updates in complete hours shared by every selected type. Gaps stay missing; accumulated distance resumes from recorded hours only. Times are UTC."}
    panel("stat", "How far did the ensemble move?",
        "Sum of accepted along-route distance across matched verified hours, in kilometers. Observed cohort distance, not an estimate of unobserved travel. Policies remain separate.",
        (0, 3, 6, 3), headline("distance"))
    panel("stat", "When did it cover the most ground?",
        "Verified UTC hour with the greatest recorded distance across the selected cohorts. Ties choose the earlier hour.",
        (6, 3, 6, 3), headline("peak"))
    panel("stat", "Who carries the movement?",
        "Each cohort's share of accepted along-route distance over the same complete hours. Multiple cadence policies remain separate.",
        (12, 3, 6, 3), headline("lead"))
    panel("stat", "How much of the story can we see?",
        "Matched verified hours / fully contained requested UTC hours. Missing, partial, conflicting, and unreconciled hours are excluded; partial boundary hours are omitted.",
        (18, 3, 6, 3), headline("coverage"))
    journey = panel("timeseries", "City Score · The distance adds up",
        "Each ribbon accumulates recorded along-route kilometers for one cohort, starting at the selected period's first comparable hour. The top edge is the ensemble's total. Missing hours remain gaps; later values resume the sum of recorded hours only. Points represent whole UTC-hour aggregates, not exact travel paths. Hover to compare cohorts.",
        (0, 6, 24, 9), cumulative_distance(), "suffix: km")
    hourly_distance = panel("timeseries", "Distance in motion · When the cohorts cover ground",
        "Accepted along-route kilometers recorded in each matched complete UTC hour, summed across vehicles in each cohort. Not speed, trip length, or reconstructed distance during missing hours.",
        (0, 15, 12, 8), timeline("distance_meters_sum"), "suffix: km / hour")
    cadence = panel("timeseries", "City Score · When the music gets dense",
        "Published tone opportunities from matched verified hours. Taller ribbons show denser musical potential, not notes heard or musical BPM.",
        (0, 34, 24, 7), timeline("published_crossings_per_hour"), "suffix: opportunities / hour")
    mix = panel("timeseries", "Who's Playing? · Who carries each hour's movement?",
        "Percentage of recorded distance supplied by each cohort in each matched hour. A zero-total-distance hour has no percentage distribution. Shares describe observed movement, not fleet size, riders, or notes heard.",
        (12, 15, 12, 8), timeline("distance_meters_sum", percentage=True), "percent")
    if enabled:
        journey["fieldConfig"]["defaults"]["custom"].update({"axisLabel": "Accumulated recorded distance · km", "lineInterpolation": "stepAfter"})
        hourly_distance["fieldConfig"]["defaults"]["custom"]["axisLabel"] = "Recorded distance · km per hour"
        cadence["fieldConfig"]["defaults"]["custom"]["axisLabel"] = "Tone opportunities per hour"
        mix["fieldConfig"]["defaults"]["max"] = 100
        mix["fieldConfig"]["defaults"]["custom"].update({"fillOpacity": 65, "axisLabel": "Share of recorded distance"})
    scatter = panel("xychart", "Movement & Music · Does more movement mean more music?",
        "Each bubble is one transit type in one verified UTC hour. Right: accepted along-route km per observed vehicle-hour. Up: published tone opportunities. Size: average observed vehicles. Hover for hour and observation counts. Hours without accepted movement updates are omitted. Association does not establish causation or vehicle speed.",
        (0, 23, 16, 11), scatter_sql())
    if enabled:
        # Modern XY mappings need a version marker; otherwise Grafana migrates
        # them as legacy string mappings and the chart cannot find its fields.
        scatter["pluginVersion"] = "13.3.0"
        scatter["transformations"] = [{"id": "partitionByValues", "options": {"fields": ["transit_type"]}}]
        scatter["options"] = {"mapping": "auto", "series": [{
            "x": {"matcher": {"id": "byName", "options": "movement"}},
            "y": {"matcher": {"id": "byName", "options": "opportunities"}},
            "size": {"matcher": {"id": "byName", "options": "vehicles"}}}],
            "legend": {"showLegend": True, "displayMode": "list", "placement": "bottom", "calcs": []},
            "tooltip": {"mode": "single"}}
        scatter["fieldConfig"]["defaults"]["custom"] = {"show": "points", "pointShape": "circle",
            "pointSize": {"fixed": 9, "min": 6, "max": 24}, "fillOpacity": 55, "pointStrokeWidth": 0}
        scatter["fieldConfig"]["overrides"] = [
            {"matcher": {"id": "byName", "options": "movement"}, "properties": [
                {"id": "unit", "value": "suffix: km / vehicle-hour"}, {"id": "decimals", "value": 1},
                {"id": "custom.axisLabel", "value": "Observed movement · km per vehicle-hour"}]},
            {"matcher": {"id": "byName", "options": "opportunities"}, "properties": [
                {"id": "unit", "value": "suffix: opportunities / hour"},
                {"id": "custom.axisLabel", "value": "Tone opportunities per hour"}]},
            {"matcher": {"id": "byName", "options": "vehicles"}, "properties": [
                {"id": "unit", "value": "suffix: vehicles"}, {"id": "decimals", "value": 0}]},
            {"matcher": {"id": "byName", "options": "hour_utc"}, "properties": [
                {"id": "displayName", "value": "Hour (UTC)"}, {"id": "unit", "value": "dateTimeAsIso"}]},
            {"matcher": {"id": "byName", "options": "distinct_vehicles"}, "properties": [
                {"id": "displayName", "value": "Vehicles observed in hour"}]},
            {"matcher": {"id": "byName", "options": "accepted_updates"}, "properties": [
                {"id": "displayName", "value": "Accepted movement updates"}]},
        ] + [{"matcher": {"id": "byRegexp", "options": f"^opportunities.*{name}.*$"},
              "properties": [{"id": "color", "value": {"mode": "fixed", "fixedColor": color}}]}
             for name, color in {"Bus": "green", "Rail": "blue", "Streetcar": "purple", "Unknown": "orange"}.items()]
    ensemble = panel("piechart", "Who's Playing? · Share of the recorded journey",
        "Each cohort's share of total accepted along-route distance over matched complete hours. Zero-distance types have no slice. Multiple collection policies make the combined donut unavailable. Independent panels do not share a database snapshot.",
        (16, 23, 8, 11), ensemble_sql(), "suffix: km")
    if enabled:
        ensemble["transformations"] = [{"id": "rowsToFields", "options": {"mappings": [
            {"fieldName": "category", "handlerKey": "field.name"},
            {"fieldName": "value", "handlerKey": "field.value"}]}}]
        ensemble["options"] = {"pieType": "donut", "displayLabels": ["name", "percent"],
            "reduceOptions": {"values": True, "calcs": [], "fields": ""},
            "legend": {"displayMode": "list", "placement": "bottom", "showLegend": True, "values": []},
            "tooltip": {"mode": "single", "sort": "none"}}

    cities = json.loads((ROOT / "src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/appsettings.json").read_text(encoding="utf-8"))["Cities"]
    variables = [
        {"name": "insights_source", "type": "constant", "hide": 2, "skipUrlSync": True,
         "query": datasource_uid or "__unconfigured__", "current": {"text": datasource_uid or "Unconfigured", "value": datasource_uid or "__unconfigured__"}},
        {"name": "city", "label": "City", "type": "query" if enabled else "custom",
         "query": discovery("city_slug") if enabled else ",".join(city["Name"] for city in cities),
         "current": {"text": "atlanta", "value": "atlanta"}, "refresh": 2 if enabled else 0,
         "multi": False, "includeAll": False, "allowCustomValue": False, "options": []},
        {"name": "category", "label": "Transit ensemble", "type": "query" if enabled else "custom",
         "query": discovery("category", "AND city_slug = ${city:sqlstring}") if enabled else "Unverified : __unverified__",
         "current": {"text": "All" if enabled else "Unverified", "value": "$__all" if enabled else "__unverified__"},
         "refresh": 2 if enabled else 0, "multi": True, "includeAll": enabled, "allowCustomValue": False, "options": []},
    ]
    add_routes(panel, variables, ds, enabled and route_history_available, default_routes)
    if enabled:
        for variable in variables:
            if variable["type"] == "query":
                variable["datasource"] = ds
    return {"uid": UID, "title": "TransitJazz — The City's Rhythm (Prototype)",
            "description": "Recorded cohort distance over time, distance shares, and Movement & Music, using matched verified category hours. Missing history remains missing.",
            "tags": ["transitjazz", "prototype", "city-category-insights"], "timezone": "utc",
            "schemaVersion": 42, "editable": True, "graphTooltip": 1, "refresh": "",
            "time": {"from": "now-7d", "to": "now"}, "timepicker": {},
            "annotations": {"list": [{"name": "Observed route catalog transitions", "enable": True,
                "datasource": ds, "iconColor": "orange", "target": {"refId": "A", "rawQuery": True,
                    "format": "table", "rawSql": catalog_markers()}}]} if enabled and route_history_available else {"list": []},
            "templating": {"list": variables}, "panels": panels}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--datasource-uid", help="Use an existing verified PostgreSQL source")
    parser.add_argument("--schema-unavailable", action="store_true", help="Keep observation queries disabled")
    parser.add_argument("--route-history-available", action="store_true", help="Enable route queries after live source preflight")
    parser.add_argument("--default-route", action="append", default=[], help="Verified initial route key (repeat up to three times)")
    args = parser.parse_args()
    if args.schema_unavailable and not args.datasource_uid:
        parser.error("--schema-unavailable requires --datasource-uid")
    if len(args.default_route)>3:
        parser.error("Choose at most three initial routes")
    dashboard = generate(args.datasource_uid, args.schema_unavailable, args.route_history_available, args.default_route)
    OUTPUT.write_text(json.dumps(dashboard, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Wrote {OUTPUT.name}: {len(dashboard['panels'])} insight panels.")
