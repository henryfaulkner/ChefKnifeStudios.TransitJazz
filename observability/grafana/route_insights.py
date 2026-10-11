"""Flat route projections sharing the deployed feature 057 evidence contract."""
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
VERSION = 'observed-city-route-hour-statistics-v1'
FROM = '($__timeFrom()::timestamptz)'
TO = '($__timeTo()::timestamptz)'
KEYS = 'ARRAY[${route:sqlstring}]::text[]'
PALETTE = ['#56A64B', '#5794F2', '#B877D9', '#FF9830', '#F2495C']


def zone_sql():
    cities = json.loads((ROOT / 'src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/appsettings.json').read_text(encoding='utf-8'))['Cities']
    return 'CASE ${city:sqlstring} ' + ' '.join(
        "WHEN '%s' THEN '%s'" % (c['Name'], c['TimeZoneId']) for c in cities) + ' END'


def discovery():
    return f"""WITH retained AS (
 SELECT DISTINCT ON (route_join_key) route_join_key, coalesce(nullif(route_short_name,''),route_join_key) AS label
 FROM public.city_route_hour_statistics
 WHERE city_slug=${{city:sqlstring}} AND category IN (${{category:sqlstring}})
 AND hour_start_utc < {TO} AND hour_start_utc + interval '1 hour' > {FROM}
 ORDER BY route_join_key, hour_start_utc DESC
), labels AS (
 SELECT *, count(*) OVER (PARTITION BY label) AS duplicates FROM retained
)
SELECT * FROM (SELECT CASE WHEN duplicates>1 THEN label || ' [' || route_join_key || ']' ELSE label END AS __text,
 route_join_key AS __value FROM labels) choices ORDER BY __text COLLATE "C", __value COLLATE "C";"""


def evidence():
    # Keep the authoritative grid and city-wide quarantine before route filtering.
    sql = (ROOT / 'specs/057-hourly-route-history/contracts/route-hour-insights.sql').read_text(encoding='utf-8')
    sql = 'WITH' + sql.split('\nWITH', 1)[1].split(',\nhourly AS', 1)[0]
    bindings = {'$1': '${city:sqlstring}', '$2': KEYS, '$3': FROM,
                '$4': TO, '$5': "'%s'" % VERSION, '$6': zone_sql()}
    for key, value in bindings.items():
        sql = sql.replace(key, value)
    # Reject unbounded/oversized requests before generating hours or reading history.
    sql = sql.replace('FROM input i\n    CROSS JOIN LATERAL generate_series(',
                      "FROM (SELECT * FROM input WHERE to_utc > from_utc AND to_utc - from_utc <= interval '31 days' AND cardinality(route_keys) BETWEEN 1 AND 5) i\n    CROSS JOIN LATERAL generate_series(")
    return sql + """, names AS (
 SELECT k.route_join_key, (ARRAY['①','②','③','④','⑤'])[row_number() OVER (ORDER BY k.route_join_key COLLATE "C")] || ' ' || coalesce(n.route_short_name,k.route_join_key) || ' [' || k.route_join_key || ']' AS label,
 row_number() OVER (ORDER BY k.route_join_key COLLATE "C") AS color_slot
 FROM route_keys k LEFT JOIN LATERAL (
 SELECT nullif(route_short_name,'') AS route_short_name FROM retained r
 WHERE r.route_join_key=k.route_join_key ORDER BY hour_start_utc DESC LIMIT 1
 ) n ON true
), eligible AS (
 SELECT * FROM evidence WHERE contributes AND distinct_active_vehicle_count IS NOT NULL
), matched AS (
 SELECT requested_hour_utc, healthy_cadence_limit_seconds
 FROM eligible GROUP BY requested_hour_utc, healthy_cadence_limit_seconds
 HAVING count(*) = (SELECT count(*) FROM route_keys)
), fingerprints AS (
 SELECT e.requested_hour_utc, m.healthy_cadence_limit_seconds,
 jsonb_agg(jsonb_build_array(e.requested_route_key,e.route_catalog_fingerprint,e.category,e.route_short_name,e.static_route_id)
 ORDER BY e.requested_route_key COLLATE "C")::text AS identity
 FROM eligible e JOIN matched m USING(requested_hour_utc,healthy_cadence_limit_seconds)
 GROUP BY e.requested_hour_utc,m.healthy_cadence_limit_seconds
), changes AS (
 SELECT *, identity IS DISTINCT FROM lag(identity) OVER (ORDER BY requested_hour_utc)
 OR healthy_cadence_limit_seconds IS DISTINCT FROM lag(healthy_cadence_limit_seconds) OVER (ORDER BY requested_hour_utc) AS changed
 FROM fingerprints
), segmented AS (
 SELECT *, sum(changed::integer) OVER (ORDER BY requested_hour_utc) AS segment FROM changes
), segments AS (
 SELECT segment,min(requested_hour_utc) AS start_utc,max(requested_hour_utc)+interval '1 hour' AS end_utc,
 count(*) AS hours FROM segmented GROUP BY segment
), chosen AS (
 SELECT * FROM segments WHERE segment::text = ${route_segment:sqlstring}
 OR (${route_segment:sqlstring}='latest' AND segment=(SELECT max(segment) FROM segments))
), comparable AS (
 SELECT e.*, n.label,n.color_slot FROM eligible e
 JOIN segmented s USING(requested_hour_utc,healthy_cadence_limit_seconds)
 JOIN chosen c USING(segment) JOIN names n ON n.route_join_key=e.requested_route_key
 WHERE (SELECT count(DISTINCT healthy_cadence_limit_seconds) FROM matched)=1
), period AS (
 SELECT requested_route_key,label,color_slot,count(*) AS comparable_hours,
 min(requested_hour_utc) AS first_hour,max(requested_hour_utc)+interval '1 hour' AS end_hour,
 sum(distance_meters_sum)/1000 AS recorded_km,
 sum(distinct_active_vehicle_count::numeric) AS route_vehicle_hours,
 sum(distance_interval_count) AS accepted_intervals,
 sum(crossings_published_count::numeric)/count(*) AS opportunities,
 sum(active_vehicle_count_sum::numeric)/nullif(sum(valid_active_sample_count),0) AS vehicles,
 sum(distance_meters_sum) FILTER (WHERE distinct_active_vehicle_count>0)/1000 AS in_service_recorded_km,
 sum(distinct_active_vehicle_count::numeric) FILTER (WHERE distinct_active_vehicle_count>0) AS in_service_route_vehicle_hours,
 count(*) FILTER (WHERE distinct_active_vehicle_count>0) AS in_service_hours,
 (sum(active_vehicle_count_sum::numeric) FILTER (WHERE distinct_active_vehicle_count>0)) /
   nullif((sum(valid_active_sample_count::numeric) FILTER (WHERE distinct_active_vehicle_count>0)),0) AS in_service_vehicles,
 sum(distance_interval_count) FILTER (WHERE distinct_active_vehicle_count>0) AS in_service_accepted_intervals
 FROM comparable GROUP BY requested_route_key,label,color_slot
)
"""


def segments_query():
    # Segment discovery cannot depend on its own variable.
    sql = evidence().split(', chosen AS', 1)[0]
    return sql + """ SELECT 'Latest comparable segment' AS __text,'latest' AS __value
 UNION ALL SELECT to_char(start_utc AT TIME ZONE 'UTC','YYYY-MM-DD HH24:MI') || ' – ' ||
 to_char(end_utc AT TIME ZONE 'UTC','YYYY-MM-DD HH24:MI') || ' UTC (' || hours || ' hours)',
 segment::text FROM segments WHERE (SELECT count(*) FROM segments)>1 ORDER BY __value DESC;"""


def context():
    return evidence() + f"""SELECT CASE
 WHEN {TO}<={FROM} OR {TO}-{FROM}>interval '31 days' THEN 'Choose a range of 31 days or less for route comparisons'
 WHEN cardinality({KEYS})>5 THEN 'Choose up to five routes to compare'
 WHEN (SELECT count(*) FROM route_keys)=0 THEN 'Choose routes with retained history'
 WHEN (SELECT count(DISTINCT healthy_cadence_limit_seconds) FROM matched)>1 THEN 'Multiple cadence policies: narrow the period'
 WHEN NOT EXISTS(SELECT 1 FROM comparable) THEN 'No comparable hours for these routes — see route coverage'
 ELSE (SELECT count(*) FROM route_keys)::text || ' routes · ' ||
 (SELECT count(DISTINCT requested_hour_utc) FROM comparable)::text || ' / ' ||
 (SELECT count(*) FROM hours WHERE fully_contained)::text || ' whole hours · ' ||
 (SELECT to_char(min(first_hour) AT TIME ZONE 'UTC','DD Mon HH24:MI') || ' – ' ||
 to_char(max(end_hour) AT TIME ZONE 'UTC','DD Mon HH24:MI') FROM period) || ' UTC · ' || {zone_sql()} ||
 ' · ' || (SELECT count(*) FROM period WHERE route_vehicle_hours=0 OR accepted_intervals=0)::text || ' bubbles omitted'
 END AS context;"""


def ranking():
    return evidence() + """SELECT label AS route, recorded_km::double precision AS recorded_km,
 (recorded_km/nullif(sum(recorded_km) OVER (),0))::double precision AS selected_share,
 comparable_hours,(SELECT count(*) FROM hours WHERE fully_contained) AS requested_hours,
 first_hour,end_hour,requested_route_key AS route_key,color_slot AS route_color FROM period
 ORDER BY recorded_km DESC, requested_route_key COLLATE "C";"""


def cumulative():
    return evidence() + """, accumulated AS (
 SELECT *, sum(distance_meters_sum) OVER (PARTITION BY requested_route_key ORDER BY requested_hour_utc)/1000 AS km
 FROM comparable
)
SELECT h.hour_start_utc AS time,a.km::double precision AS value,
 (a.distance_meters_sum/1000)::double precision AS hourly_km,n.label AS metric
FROM hours h CROSS JOIN names n LEFT JOIN accumulated a
 ON a.requested_hour_utc=h.hour_start_utc AND a.requested_route_key=n.route_join_key
ORDER BY time,metric;"""


def local_hours():
    return evidence() + """, buckets AS (
 SELECT label, extract(hour FROM local_start)::integer AS hour,
 sum(distance_meters_sum)/count(*)/1000 AS mean_km, count(*) AS utc_hours,
 min(local_start::date) AS first_date,max(local_start::date) AS last_date
 FROM comparable GROUP BY label,extract(hour FROM local_start)
)
SELECT lpad(h::text,2,'0') AS local_hour,n.label AS route,b.mean_km::double precision AS mean_km,
 b.utc_hours,b.first_date,b.last_date FROM generate_series(0,23) h CROSS JOIN names n
 LEFT JOIN buckets b ON b.hour=h AND b.label=n.label ORDER BY h,n.label;"""


def bubble():
    return evidence() + """SELECT label AS route,(recorded_km/nullif(route_vehicle_hours,0))::double precision AS movement,
 opportunities::double precision AS opportunities,vehicles::double precision AS vehicles,
 route_vehicle_hours,accepted_intervals,comparable_hours,first_hour,end_hour
 FROM period WHERE route_vehicle_hours>0 AND accepted_intervals>0 ORDER BY requested_route_key COLLATE "C";"""


def movement_volume_bubble():
    return evidence() + """SELECT label AS route,
 (in_service_recorded_km/nullif(in_service_route_vehicle_hours,0))::double precision AS avg_km_per_vehicle_hour,
 (in_service_recorded_km/nullif(in_service_hours,0))::double precision AS avg_route_km_per_hour,
 in_service_vehicles::double precision AS vehicles,
 in_service_hours,comparable_hours,in_service_route_vehicle_hours,
 in_service_accepted_intervals,first_hour,end_hour
 FROM period WHERE in_service_hours>0 AND in_service_route_vehicle_hours>0
 ORDER BY requested_route_key COLLATE "C";"""


def coverage():
    return evidence() + """SELECT e.requested_hour_utc AS time,e.requested_hour_utc+interval '1 hour' AS time_end,
 n.label AS route,e.effective_coverage AS state,
 EXISTS(SELECT 1 FROM comparable c WHERE c.requested_hour_utc=e.requested_hour_utc AND c.requested_route_key=e.requested_route_key) AS contributes,
 CASE WHEN NOT e.fully_contained THEN 'Partial request boundary'
 WHEN NOT e.definition_compatible AND e.hour_start_utc IS NOT NULL THEN 'Unsupported definition'
 WHEN e.effective_coverage<>'Complete' THEN e.effective_coverage
 WHEN NOT EXISTS(SELECT 1 FROM matched m WHERE m.requested_hour_utc=e.requested_hour_utc) THEN 'Not shared by selected routes'
 WHEN (SELECT count(DISTINCT healthy_cadence_limit_seconds) FROM matched)>1 THEN 'Multiple cadence policies'
 WHEN NOT EXISTS(SELECT 1 FROM comparable c WHERE c.requested_hour_utc=e.requested_hour_utc) THEN 'Outside selected history segment'
 ELSE 'Contributes' END AS explanation,
 e.incomplete_reasons::text,e.route_catalog_fingerprint,e.catalog_changed,e.definition_version,e.healthy_cadence_limit_seconds
 FROM evidence e JOIN names n ON n.route_join_key=e.requested_route_key ORDER BY time,route;"""


def catalog_markers():
    return evidence() + """, transitions AS (
 SELECT requested_route_key,requested_hour_utc,route_catalog_fingerprint,
 lag(route_catalog_fingerprint) OVER (PARTITION BY requested_route_key ORDER BY requested_hour_utc) AS previous_fingerprint,
 lag(requested_hour_utc) OVER (PARTITION BY requested_route_key ORDER BY requested_hour_utc) AS previous_hour
 FROM evidence WHERE route_catalog_fingerprint IS NOT NULL
)
SELECT previous_hour+interval '1 hour' AS time,requested_hour_utc AS timeend,
 'Catalog transition: ' || requested_route_key || CASE WHEN requested_hour_utc>previous_hour+interval '1 hour'
 THEN ' (change time unknown across capture gap)' ELSE ' (observed boundary)' END AS text
 FROM transitions WHERE previous_fingerprint IS NOT NULL AND previous_fingerprint<>route_catalog_fingerprint;"""


def add_routes(panel, variables, ds, enabled, defaults=()):
    original_panel = panel
    def panel(*args, **kwargs):
        item = original_panel(*args, **kwargs)
        if not enabled:
            for target in item.get('targets', []):
                target['hide'] = True
            item['type'] = 'text'
            item['options'] = {'mode':'markdown','content':'Route observations pending live schema and retained-history verification.'}
        return item
    variables.extend([
        {'name':'route','label':'Routes (choose 1–5)','type':'query' if enabled else 'custom',
         'query':discovery() if enabled else 'Pending : __pending__', 'datasource':ds,
         'multi':True,'includeAll':False,'allowCustomValue':True,'refresh':2 if enabled else 0,
         'current':{'text':list(defaults), 'value':list(defaults)},'options':[]},
        {'name':'route_segment','label':'Route history segment','type':'query' if enabled else 'custom',
         'query':segments_query() if enabled else 'Latest : latest','datasource':ds,'multi':False,
         'includeAll':False,'refresh':2 if enabled else 0,'current':{'text':'Latest comparable segment','value':'latest'},'options':[]},
        {'name':'route_zone','type':'query' if enabled else 'constant','hide':2,'skipUrlSync':True,
         'query':f'SELECT {zone_sql()} AS __text, {zone_sql()} AS __value;' if enabled else 'UTC',
         'datasource':ds,'refresh':1 if enabled else 0,'options':[]},
    ])
    intro = panel('stat','Routes behind the rhythm',
        "Follow a few routes behind the city's rhythm. Compare recorded distance, daily patterns, and musical activity over shared observed hours. Gaps represent unavailable history; distance comes from accepted movement updates. Independent panels are an exploration view. Route-vehicle-hours are exact hourly populations, not unique period vehicles. Published opportunities do not measure notes heard.",
        (0,41,24,3),context())
    bars = panel('barchart','Which selected routes carry the distance?',
        'Recorded distance during hours shared by the selected routes. Shares refer to the selected routes only.',
        (0,44,8,10),ranking(),'suffix: km')
    curve = panel('timeseries','Watch the routes accumulate distance',
        'Accumulated recorded movement; missing hours add no estimated distance. Select a history segment to restart across catalog changes.',
        (8,44,16,10),cumulative(),'suffix: km')
    strip = panel('state-timeline','How much of each route can we see?',
        'Each route’s own retained coverage. Complete hours can be excluded from comparison. Missing membership is unknown. Catalog transitions across gaps have uncertain timing.',
        (0,54,24,6),coverage())
    daily = panel('barchart','When does each route find its rhythm? · ${route_zone:text}',
        'Observed pattern for the selected dates. Accepted kilometers / contributing UTC hours in the named local zone. Repeated DST hours contribute independently; empty buckets stay unavailable.',
        (0,60,12,10),local_hours(),'suffix: km / hour')
    xy = panel('xychart','Which routes turn movement into musical activity?',
        'Recorded movement and published musical opportunities; the relationship does not establish causation. Weighted period ratios over common hours. Bubbles omit zero populations and missing movement intervals.',
        (12,60,12,10),bubble())
    movement_xy = panel('xychart','Route distance · Per-vehicle movement vs hourly route distance',
        'Each bubble is one selected route. X is accepted along-route km per observed route-vehicle-hour; Y is average total route km per observed in-service hour; size is the sample-weighted mean observed vehicles. A route is in service here when at least one vehicle was observed in a complete hour; this telemetry proxy does not establish scheduled service. In-service hours with zero movement remain included.',
        (0,70,24,10),movement_volume_bubble())
    if not enabled:
        return
    intro['options']['text']={'valueSize':18}
    for item in (bars,curve,daily,xy,movement_xy):
        item['fieldConfig']['defaults']['decimals']=2
        item['fieldConfig']['overrides']=[]
        item['fieldConfig']['overrides']=[{'matcher':{'id':'byRegexp','options':f'.*{symbol}.*'},'properties':[{'id':'color','value':{'mode':'fixed','fixedColor':color}}]} for symbol,color in zip('①②③④⑤',PALETTE)]
    bars['options']={'orientation':'horizontal','xField':'route','colorByField':'route_color','showValue':'always','stacking':'none','tooltip':{'mode':'single'},'legend':{'showLegend':False}}
    bars['fieldConfig']['overrides']=[{'matcher':{'id':'byName','options':f},'properties':[{'id':'custom.hideFrom','value':{'viz':True,'tooltip':False,'legend':True}}]} for f in ('selected_share','comparable_hours','requested_hours')]
    bars['fieldConfig']['overrides'].append({'matcher':{'id':'byName','options':'route_color'},'properties':[{'id':'custom.hideFrom','value':{'viz':True,'tooltip':True,'legend':True}},{'id':'mappings','value':[{'type':'value','options':{str(i+1):{'color':color,'text':str(i+1)} for i,color in enumerate(PALETTE)}}]}]})
    bars['fieldConfig']['overrides'] += [{'matcher':{'id':'byName','options':'selected_share'},'properties':[{'id':'unit','value':'percentunit'}]}]
    bars['fieldConfig']['defaults']['links']=[{'title':'Follow this route','url':'/d/transitjazz-category-insights-lab/?${__url_time_range}&${city:queryparam}&${category:queryparam}&var-route=${__data.fields.route_key:percentencode}&timezone=utc'}]
    curve['fieldConfig']['defaults']['custom'].update({'stacking':{'mode':'none','group':'A'},'lineInterpolation':'stepAfter','fillOpacity':0,'axisLabel':'Accumulated recorded distance · km','showPoints':'always'})
    curve['fieldConfig']['overrides'].append({'matcher':{'id':'byRegexp','options':'^hourly_km.*'},'properties':[{'id':'custom.hideFrom','value':{'viz':True,'tooltip':False,'legend':True}},{'id':'displayName','value':'Hourly distance · ${__field.labels.metric}'}]})
    strip['transformations']=[{'id':'partitionByValues','options':{'fields':['route']}}]
    strip['fieldConfig']['overrides']=[{'matcher':{'id':'byName','options':f},'properties':[{'id':'custom.hideFrom','value':{'viz':True,'tooltip':False,'legend':True}}]} for f in ('route','contributes','explanation','incomplete_reasons','route_catalog_fingerprint','catalog_changed','definition_version','healthy_cadence_limit_seconds')]
    strip['options']={'mergeValues':False,'showValue':'never','rowHeight':0.8,'legend':{'showLegend':True,'displayMode':'list','placement':'bottom'},'tooltip':{'mode':'single'}}
    strip['fieldConfig']['defaults'].update({'unit':'none','color':{'mode':'fixed','fixedColor':'gray'},'mappings':[{'type':'value','options':{s:{'text':s,'color':c} for s,c in {'Complete':'green','Partial':'yellow','NoData':'blue','Conflict':'red','Missing':'gray'}.items()}}]})
    daily['transformations']=[{'id':'groupingToMatrix','options':{'rowField':'local_hour','columnField':'route','valueField':'mean_km','emptyValue':'null'}}]
    daily['options']={'orientation':'vertical','showValue':'never','stacking':'none','tooltip':{'mode':'multi'},'legend':{'showLegend':True,'placement':'bottom'}}
    daily['fieldConfig']['overrides'].append({'matcher':{'id':'byRegexp','options':'^local_hour'},'properties':[{'id':'unit','value':'none'}]})
    daily['fieldConfig']['defaults']['unit']='none'
    for override in daily['fieldConfig']['overrides'][:5]:
        override['properties'].append({'id':'unit','value':'suffix: km / hour'})
    xy['pluginVersion']='13.3.0'
    xy['transformations']=[{'id':'partitionByValues','options':{'fields':['route']}}]
    xy['options']={'mapping':'auto','series':[{'x':{'matcher':{'id':'byName','options':'movement'}},'y':{'matcher':{'id':'byName','options':'opportunities'}},'size':{'matcher':{'id':'byName','options':'vehicles'}}}], 'legend':{'showLegend':True,'placement':'bottom','displayMode':'list'},'tooltip':{'mode':'single'}}
    xy['fieldConfig']['defaults']['custom']={'show':'points','pointShape':'circle','pointSize':{'fixed':9,'min':6,'max':24},'fillOpacity':55}
    xy['fieldConfig']['overrides'] += [{'matcher':{'id':'byName','options':'movement'},'properties':[{'id':'unit','value':'suffix: km / route-vehicle-hour'},{'id':'custom.axisLabel','value':'km / observed route-vehicle-hour'}]}, {'matcher':{'id':'byName','options':'opportunities'},'properties':[{'id':'unit','value':'suffix: opportunities / hour'},{'id':'custom.axisLabel','value':'Published tone opportunities / hour'}]}]
    movement_xy['pluginVersion']='13.3.0'
    movement_xy['fieldConfig']['defaults']['noValue']='No observed in-service route hours'
    movement_xy['transformations']=[{'id':'partitionByValues','options':{'fields':['route']}}]
    movement_xy['options']={'mapping':'auto','series':[{'x':{'matcher':{'id':'byName','options':'avg_km_per_vehicle_hour'}},'y':{'matcher':{'id':'byName','options':'avg_route_km_per_hour'}},'size':{'matcher':{'id':'byName','options':'vehicles'}}}], 'legend':{'showLegend':True,'placement':'bottom','displayMode':'list'},'tooltip':{'mode':'single'}}
    movement_xy['fieldConfig']['defaults']['custom']={'show':'points','pointShape':'circle','pointSize':{'fixed':9,'min':6,'max':24},'fillOpacity':55,'pointStrokeWidth':0}
    movement_xy['fieldConfig']['overrides'] += [
        {'matcher':{'id':'byName','options':'avg_km_per_vehicle_hour'},'properties':[{'id':'unit','value':'none'},{'id':'custom.axisLabel','value':'Average km / observed vehicle-hour'}]},
        {'matcher':{'id':'byName','options':'avg_route_km_per_hour'},'properties':[{'id':'unit','value':'none'},{'id':'custom.axisLabel','value':'Average route km / observed in-service hour'}]},
        {'matcher':{'id':'byName','options':'vehicles'},'properties':[{'id':'unit','value':'suffix: vehicles'},{'id':'decimals','value':1}]}
    ]
