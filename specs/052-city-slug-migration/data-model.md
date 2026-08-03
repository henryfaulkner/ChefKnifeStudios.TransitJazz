# Phase 1 Data Model: City Slug Migration

No persistent schema changes. This feature changes the **value** of an identifier that flows
through several in-memory and wire structures, and **splits one property into two** so that
telemetry can hold a different value from everything else.

---

## E1. City Slug (value)

The permanent public identifier for a city. Not a new type — the value carried by
`CityNames.*` constants.

| Old value | New value | City |
|---|---|---|
| `marta` | `atlanta` | Atlanta, GA |
| `wmata` | `washington-dc` | Washington, DC |
| `mbta` | `boston` | Boston, MA |
| `nymta` | `new-york-city` | New York, NY |
| `ttc` | `toronto` | Toronto, ON |
| `septa` | `philadelphia` | Philadelphia, PA |
| `rtd` | `denver` | Denver, CO |

**Validation rules**
- Lowercase ASCII, digits, and `-` only; no spaces, no underscores, no trailing/leading `-`.
- Full city name; region suffix **only** to disambiguate (`washington-dc`).
- Must be unique across the registry.
- Must round-trip through URL-fragment escaping unchanged.

**Consumers** (all take the same literal, per FR-004): URL fragment · SignalR group name ·
`?city=` query parameter · `Cities[].Name` config key · route-shape store prefix `{city}:` ·
Umami pageview path.

---

## E2. City Registry — `CityNames`

`src/ChefKnifeStudios.TransitJazz.Shared/CityNames.cs`

Constant **names** stay agency-flavoured (`CityNames.Marta`); only their **values** change.
Renaming the C# identifiers too is optional churn across ~55 files and is **out of scope** —
the identifier is internal, the value is public.

```
Marta = "atlanta"        Wmata = "washington-dc"    Mbta  = "boston"
Nymta = "new-york-city"  Ttc   = "toronto"          Septa = "philadelphia"
Rtd   = "denver"
```

**Invariant**: no slug literal may exist outside this class. Currently violated by
`CityFab.razor` (7 literals) and both `appsettings.json` files (7 each) — see contracts.

---

## E3. Telemetry City Name (NEW — the split)

The critical structural change. Today one property serves both roles:

```
ITransitCity.Name ──┬─→ SignalR group, ?city=, config key, shape prefix   (becomes slug)
                    └─→ TelemetryEvent.city_name                          (must NOT change)
```

After the split:

```
ITransitCity.Name          → "atlanta"   (slug; all live boundaries)
ITransitCity.TelemetryName → "MARTA"     (agency; parquet only)
```

| Field | Value form | Example | Changes in this feature? |
|---|---|---|---|
| `Name` | lowercase slug | `atlanta` | **Yes** |
| `TelemetryName` | uppercase agency | `MARTA` | **No — frozen** |

**Rules**
- `TelemetryName` is written **only** to `TelemetryEvent.city_name` (`Worker.cs:103`, via
  `CityTickResult.CityName`).
- `TelemetryName` MUST NOT reach any SignalR group, query parameter, config key, or URL.
- Values are frozen at their current uppercase agency strings for all seven cities.
- Existing telemetry test fixtures asserting `"MARTA"` are **correct and must not be updated**.

**Rationale**: FR-016/FR-017 require an unbroken parquet history and protect the 051 Phase 3
baseline (FR-023). Without this split, renaming `Name` silently rewrites `city_name`.

---

## E4. City Configuration Entry

`Cities[]` in `TransitDataWorker/appsettings.json` and `WebAPI/appsettings.json`.

Only `Name` changes; every sibling key (feed URLs, `RailRouteIdMap`, static zip URLs) is
untouched. The two arrays MUST stay byte-identical in their `Name` values — the parity check
(FR-006) enforces this, because a one-sided edit means the worker publishes to a group no
client joins, with no error anywhere (FR-008, SC-003).

`appsettings.json` carries **no** telemetry name; `TelemetryName` lives in code (E3), keyed off
the city, so config cannot drift from it.

---

## E5. City Copy Set

30 agency-prefixed keys in `RouteFilterResources.resx` — six cities × five keys
(`*AudioOverlayHeader`, `*AudioOverlayParagraph1‑3`, `*OverlayParagraph1`). Atlanta has none;
it is the default arm of both switch expressions.

Prefixes become city-flavoured PascalCase:

| Old prefix | New prefix |
|---|---|
| `Wmata*` | `WashingtonDc*` |
| `Mbta*` | `Boston*` |
| `Nymta*` | `NewYorkCity*` |
| `Ttc*` | `Toronto*` |
| `Septa*` | `Philadelphia*` |
| `Rtd*` | `Denver*` |

Resx keys are internal; renaming them is **cosmetic and user-invisible**. The switch arms in
`AudioUnlockOverlay.razor:263‑268` and `InfoFab.razor:48‑53` must move in lockstep — a
half-rename yields a missing string, which FR-012/SC-005 forbid.

---

## E6. Hub Method Name

`HubMethods.JoinCity` → `JoinCityV2` (`"JoinCity"` → `"JoinCityV2"`), with `TransitHub.JoinCity`
renamed to match. Contrary to the source assessment, **no `V2` variant exists today** and there
is no `LeaveCity` method at all.

Purpose is solely the FR-009 version gate: an old client invoking `"JoinCity"` against an
updated hub fails the invocation loudly, instead of joining a group nobody publishes to and
showing an empty map. Method **signature is unchanged**; only the name gates.

---

## Entity relationships

```
CityNames (E2) ──defines──> City Slug (E1)
       │
       ├──> appsettings Cities[].Name (E4)  ──> SignalR group ──┐
       ├──> URL fragment / ?city= / {city}: prefix              ├─ must all agree (FR-008)
       └──> ITransitCity.Name ──────────────────────────────────┘
                    │
                    └── SPLIT ──> ITransitCity.TelemetryName (E3) ──> city_name  [FROZEN]

CityNames (E2) ──switch arms──> City Copy Set (E5)
HubMethods (E6) ──gates──> join handshake
```

## Out of scope

- Renaming the `city_name` **column**, or adding a separate agency dimension.
- Restructuring `Cities[]` into a city-with-agencies shape (step two).
- Renaming `CityNames` C# identifiers or `MartaCity`/`NymtaCity` class names.
- Any change to `tools/telemetry-mcp` — its allow-list stays valid because `city_name`'s name,
  type, and values are all unchanged (FR-019).
