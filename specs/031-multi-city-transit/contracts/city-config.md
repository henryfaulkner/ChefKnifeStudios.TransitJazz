# Contract: `Cities:` configuration array

**Location**: worker `appsettings.json` / `appsettings.Development.json`
(`src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/`). Replaces the flat `Marta:`
block.

## Shape

```jsonc
"Cities": [
  {
    "Name": "marta",
    "GtfsRtUrls": [ "https://gtfs-rt.itsmarta.com/.../vehiclepositions.pb" ],
    "RailRealtime": { "BaseUrl": "https://developerservices.itsmarta.com:18096/...", "Enabled": true },
    "StaticZipUrls": [ "https://.../marta-gtfs.zip" ],
    "EmitsTelemetry": true
    // resolved to the named MartaCity impl (bespoke JSON rail)
  },
  {
    "Name": "wmata",
    "GtfsRtUrls": [
      "https://api.wmata.com/gtfs/bus-gtfsrt-vehiclepositions.pb",
      "https://api.wmata.com/gtfs/rail-gtfsrt-vehiclepositions.pb"
    ],
    "StaticZipUrls": [
      "https://api.wmata.com/gtfs/bus-gtfs-static.zip",
      "https://api.wmata.com/gtfs/rail-gtfs-static.zip"
    ],
    "ApiKeyEnvVar": "WMATA_API_KEY",
    "RailRouteIdMap": { "BLUE":"B","GREEN":"G","ORANGE":"O","RED":"R","SILVER":"S","YELLOW":"Y" },
    "EmitsTelemetry": false
    // resolved to the generic GtfsRtCity — no code
  }
]
```

## Field contract

| Field | Type | Required | Default | Rule |
|---|---|---|---|---|
| `Name` | string | ✔ | — | lowercase, unique |
| `GtfsRtUrls` | string[] | ✔ | — | ≥1 entry |
| `StaticZipUrls` | string[] | ✔ | — | ≥1 entry |
| `RailRealtime` | `{BaseUrl,Enabled}` | ✖ | absent | bespoke impls only |
| `RailRouteIdMap` | map<string,string> | ✖ | empty | applied by `GtfsRtCity` |
| `ApiKeyEnvVar` | string | ✖ | none | **name** of env var; value is a CA secret, never committed |
| `EmitsTelemetry` | bool | ✔ | — | → `ITransitCity.EmitsTelemetry` |

## Secret contract (Principle II / FR-014 / SC-008)

- `appsettings.json` MUST contain only `ApiKeyEnvVar` (the env-var **name**), never the key value.
- The key value is supplied as a Container Apps secret surfaced as that env var at runtime.
- **Reject vector**: a literal `api_key`/`ApiKey` value committed in any `appsettings*.json` fails
  the security gate.

## Registry resolution (Q8)

```
for each entry in Cities:
  if a named ITransitCity is registered for entry.Name  -> use it (inject entry)
  else                                                  -> new GtfsRtCity(entry)
```

Adding a standard city = append a `Cities:` entry (+ CA secret if keyed). **Zero C#** (SC-002).
