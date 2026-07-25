# Quickstart: Add Boston (MBTA)

The entire change. ~4 config edits + 2 source edits. No new files.

## Step 1 — Add the `mbta` entry to all four `Cities:` arrays

Append this object to the `Cities` array in each of:

- `src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/appsettings.json`
- `src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/appsettings.Development.json`
- `src/Server/ChefKnifeStudios.MartaJazz.Server.WebAPI/appsettings.json`
- `src/Server/ChefKnifeStudios.MartaJazz.Server.WebAPI/appsettings.Development.json`

```json
{
  "Name": "mbta",
  "GtfsRtUrls": [ "https://cdn.mbta.com/realtime/VehiclePositions.pb" ],
  "StaticZipUrls": [ "https://cdn.mbta.com/MBTA_GTFS.zip" ],
  "EmitsTelemetry": false
}
```

(Same object in all four — the worker and WebAPI read the same shape. WebAPI ignores `GtfsRtUrls`/`EmitsTelemetry`; harmless.)

## Step 2 — Add the `CityNames.Mbta` constant

`src/ChefKnifeStudios.MartaJazz.Shared/CityNames.cs`:

```csharp
public static class CityNames
{
    public const string Marta = "marta";
    public const string Wmata = "wmata";
    public const string Mbta = "mbta";   // ADD
}
```

## Step 3 — Add the Boston entry to the city picker

`src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/Components/FABs/CityFab.razor` — add a list item next to the existing two and a handler mirroring `HandleWmataClicked`:

```razor
<MatListItem>
    <MatButton Label="Boston, MA" Mini="true" @onclick="HandleMbtaClicked" Disabled="@(CurrentCity == CityNames.Mbta)" />
</MatListItem>
```

```csharp
async Task HandleMbtaClicked()
{
    await JS.InvokeVoidAsync("eval", $"location.hash='mbta';location.reload()");
}
```

## Verification

**Worker (data flowing)** — run the worker; logs show MBTA vehicles fetched and published each cycle, no exception for `{City}=mbta`. ~300 vehicles expected.

**WebAPI (shapes loaded)** — on startup, `GtfsStaticLoader` logs `city mbta loaded N route shapes` (N in the high hundreds; ~373 routes have shapes).

**Client (US1 — view Boston)** — open the app at `…/#mbta`. Map shows Boston vehicles on Boston routes including live Red/Orange/Blue trains; audio + route pills are Boston's.

**Client (US2 — pick Boston)** — open the city picker FAB; "Boston, MA" appears; selecting it reloads scoped to Boston; the active city's item is disabled.

**Isolation (SC-001)** — open `…/#marta` in a second tab; confirm zero Boston vehicles bleed into Atlanta and vice versa.

**Heavy rail with no remap (SC-002)** — confirm Red/Orange/Blue trains render on the correct lines with **no** `RailRouteIdMap` in the MBTA config.

**No secret (SC-007)** — confirm the MBTA config has no `ApiKeyEnvVar` and no key appears anywhere committed.

**No regression (SC-004)** — Atlanta and DC behave exactly as before.
