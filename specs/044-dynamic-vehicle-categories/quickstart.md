# Quickstart: Dynamic Per-City Vehicle Categories

How to build, run, and verify this feature — including the Toronto win, the existing-city regression guard, and the visible map change.

## Prerequisites

- .NET 10 SDK; the solution builds today (`ChefKnifeStudios.TransitJazz.sln`).
- Ability to run the WebAPI + TransitDataWorker + Blazor WASM client locally (or via AppHost/Aspire).
- The TTC (Toronto) city already configured with GTFS-RT feeds (feature 043).

## Build & test

```powershell
dotnet build
# Retyped classifier + wire round-trip live here:
dotnet test --filter "FullyQualifiedName~GtfsStaticLoaderTests"
dotnet test --filter "FullyQualifiedName~EventEnvelopeMessagePackTests"
```

Expected after implementation:
- `GtfsStaticLoaderTests` fixtures use `(…, string Category, int RouteType)` tuples; TTC-shaped cases assert `route_type 0→"streetcar"`, `1→"rail"`, `3→"bus"`, plus an unmapped-`route_type` case asserting `"bus"` + a warning log.
- `EventEnvelopeMessagePackTests` round-trips `Key(10)` as a **string** category.

## Config change to apply (WebAPI only)

Add to the `ttc` entry in `Server.WebAPI/appsettings.json` (see `contracts/category-config.md`):
```json
"RouteTypeCategories": { "0": "streetcar", "1": "rail", "3": "bus" }
```
Leave MARTA/WMATA/MBTA/NYMTA untouched (they use the fallback). No edit to `appsettings.Development.json` (it has no `ttc` entry).

## Run

Start WebAPI, then Worker, then client (or `dotnet run` the AppHost). Because this is a breaking wire change, run **all** on this branch together — a mixed old/new pair mis-deserializes `Key(10)`.

## Verify — Toronto (the feature win, SC-001)

1. Open the app on **Toronto (TTC)**.
2. Open the route filter panel → a **Streetcar** section appears, ordered **first**, ahead of Rail and Bus (`route_type=0` sorts first — D8).
3. With streetcars in service, the running-count label shows a **"streetcars running"** row, separate from bus and rail rows.
4. Select only the Streetcar section → only streetcar routes/vehicles stay emphasized; other categories deselect (persistent multi-selection intact).
5. Clear selection → returns to unscoped (all categories).

## Verify — existing cities unchanged (regression guard, SC-002)

On **MARTA** (and spot-check WMATA/MBTA/NYMTA):
1. Filter panel shows exactly **Rail** then **Bus** — same order, same labels as before.
2. Count label reads **"trains running"** / **"buses running"** with correct counts (copy preserved verbatim).
3. No Streetcar/Unknown section appears (none configured, none unmatched under normal conditions).

## Verify — Unknown category (SC-006)

Simulate/force a vehicle whose route isn't in the catalog (e.g. a transient RT route with no static shape):
1. It appears under an **Unknown** section + count row — **not** added to the Bus count.
2. Confirm it renders readably even without a bespoke `unknown` label/CSS (neutral styling, fallback phrase — SC-007).

## Verify — map (visible change, SC-009 / FR-017)

1. On any city with rail, compare vehicle dots before/after: **rail dots now render larger** (radius 9 / stroke 2) than bus/streetcar dots (6 / 1). This is the intended fix of the latent capital-`'Rail'` mismatch — expected, not a regression.
2. Confirm streetcar and bus dots are the same (small) size — binary tier, per-category sizing deferred.
3. Toggle the GIS basemap (settings) → after `setStyle`, dots keep their category-based sizing (the `setStyle`-restore paint block was re-keyed too — Principle VII).

## Verify — dark mode (Principle XIII)

Toggle dark mode → filter sections and count-row icons render correctly for every category in **both** themes (migrated `[data-category]` selectors + neutral default).

## Deploy note (Principle V / D14)

Breaking MessagePack change: deploy **server (WebAPI + Worker) and client atomically** in one window per the project's wire-contract discipline (`project_signalr_wire_deploy_constraint`; MartaJazz ships from `deploy/marta-jazz`). No dual-field transition, no backward-compat shim.
