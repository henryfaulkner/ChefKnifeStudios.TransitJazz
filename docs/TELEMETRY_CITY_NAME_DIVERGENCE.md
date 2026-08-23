# Telemetry city-name parity

`TelemetryEvent.city_name` and `TelemetryEvent.cities_processed_csv` use the same canonical city slug as every other city boundary: URL fragments, SignalR groups, `?city=` parameters, city configuration, and route-shape store prefixes.

`ITransitCity.Name` is the sole city identifier. The Worker writes that value to each per-city telemetry event and collects it for the full-cycle event, so a separate telemetry-only label cannot drift from the routing label.

| City | Telemetry label |
|---|---|
| Atlanta | `atlanta` |
| Washington, DC | `washington-dc` |
| Boston | `boston` |
| New York City | `new-york-city` |
| Toronto | `toronto` |
| Philadelphia | `philadelphia` |
| Denver | `denver` |

Existing immutable parquet files retain the identifiers written when they were produced. New telemetry written after this change uses the canonical city slugs.
