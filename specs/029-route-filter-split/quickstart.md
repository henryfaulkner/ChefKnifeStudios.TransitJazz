# Quickstart: RouteFilter Rail / Bus Split

Manual verification (no automated UI test suite in this repo).

## Prereqs

- Run the app (AppHost / Aspire) with the WebAPI serving GTFS static shapes and the worker feeding
  rail + bus vehicles. MARTA rail operating hours give live trains; static grouping works regardless.

## Steps

1. **Build the solution.** `dotnet build` — confirm Shared, Server.WebAPI, Client.Shared compile
   with the new `Mode` field.
2. **Open the app** and bring up the route filter.
3. **First-paint grouping (FR-001/FR-002)**: Immediately on filter appearance — before any vehicle
   data — confirm RED/GOLD/BLUE/GREEN sit under the **Rail** label and all numbered bus routes sit
   under the **Buses** label. Rail must NOT briefly flash among bus pills.
4. **Labels (FR-005)**: Confirm the two section labels read "Rail" and "Buses" (from resx).
5. **Selection parity (FR-007/FR-008)**: Click a rail pill → all non-selected pills across **both**
   sections dim; the map filters to that route. Click a bus pill too → still one global dimming pool.
6. **Selection survives section structure**: With a rail pill selected, confirm it keeps its Rail
   section placement (Mode copied through on rebuild).
7. **Clear (FR-009)**: The Clear control stays in its row above both sections, always visible; using
   it clears selections in both sections.
8. **Empty section hide (FR-006)**: (If testable) with only bus routes loaded, confirm the Rail label
   + row do not appear; symmetric for rail-only.

## Pass criteria → spec mapping

| Step | FR / SC |
|---|---|
| 3 | FR-001, FR-002, SC-001, SC-002 |
| 4 | FR-005 |
| 5–6 | FR-007, FR-008, SC-003 |
| 7 | FR-009 |
| 8 | FR-006, SC-004 |
