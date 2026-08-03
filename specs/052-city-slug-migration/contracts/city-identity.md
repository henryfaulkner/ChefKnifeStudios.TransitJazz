# Contract: City Identity

Defines the slug rule, the seven values, and the identity/telemetry split. Binding on this
feature and on every future city added by hand or by `discover-transit-city`.

---

## C1. Slug rule

> **Full city name, lowercase, hyphen-separated. Region suffix only where needed to
> disambiguate.**

| Rule | Requirement |
|---|---|
| Character set | `a`–`z`, `0`–`9`, `-` only |
| Separator | single `-` between words; never `_`, never a space |
| Boundaries | no leading/trailing `-`, no `--` |
| Casing | lowercase at rest; input normalized by `ToLowerInvariant()` |
| Form | the city's full common name (`philadelphia`, not `philly`) |
| Region suffix | only to disambiguate (`washington-dc`); omitted otherwise |
| Uniqueness | unique across the registry |
| Stability | permanent once shipped — a shared URL, group name, and config key |

**Conforming**: `atlanta` · `boston` · `denver` · `toronto` · `philadelphia` ·
`washington-dc` · `new-york-city`

**Non-conforming**: `Atlanta` (case) · `new_york_city` (underscore) · `nyc` (abbreviation) ·
`philly` (colloquial) · `washington-dc-` (trailing) · `wmata` (agency, not place)

---

## C2. The seven values

| Constant | Old | New | Picker label (unchanged) |
|---|---|---|---|
| `CityNames.Marta` | `marta` | `atlanta` | Atlanta, GA |
| `CityNames.Wmata` | `wmata` | `washington-dc` | Washington DC |
| `CityNames.Mbta` | `mbta` | `boston` | Boston, MA |
| `CityNames.Nymta` | `nymta` | `new-york-city` | New York, NY |
| `CityNames.Ttc` | `ttc` | `toronto` | Toronto, ON |
| `CityNames.Septa` | `septa` | `philadelphia` | Philadelphia, PA |
| `CityNames.Rtd` | `rtd` | `denver` | Denver, CO |

Constant **identifiers** are unchanged (`CityNames.Marta` still resolves Atlanta). Only values
move. Picker labels were already city names and need no edit.

---

## C3. Single source of truth

**MUST**: every slug literal originates from `CityNames`.

**MUST NOT**: a slug string appear anywhere else in C#, Razor, or JS.

Known violations to fix:

| Location | Violation |
|---|---|
| `CityFab.razor:48,55,60,65,70,75,80` | `location.hash='marta'` ×7 inside `eval` |
| `TransitDataWorker/appsettings.json:4,14,28,34,59,65,71` | `Cities[].Name` ×7 |
| `WebAPI/appsettings.json:34,44,71,77,102,109,115` | `Cities[].Name` ×7 |

Config files cannot reference C# constants; they are the accepted exception, guarded by the
parity check (C5) rather than by construction.

**Not a violation**: `city_name = "MARTA"` in test fixtures — that is the telemetry value (C4),
deliberately different, and must stay.

---

## C4. Identity / telemetry split

Two distinct members after this feature:

| Member | Value | Consumers | Changes? |
|---|---|---|---|
| `ITransitCity.Name` | slug (`atlanta`) | SignalR group, `?city=`, config key, `{city}:` prefix, URL | **Yes** |
| `ITransitCity.TelemetryName` | agency (`MARTA`) | `TelemetryEvent.city_name` **only** | **No — frozen** |

**MUST**: `Worker.cs:103` write `TelemetryName`, not `Name`.
**MUST NOT**: `TelemetryName` reach any group name, query parameter, config key, or URL.
**MUST NOT**: this feature alter any existing `city_name` value, or the column's name or type.

Frozen values: `MARTA` · `WMATA` · `MBTA` · `NYMTA` · `TTC` · `SEPTA` · `RTD` (verify each
against current production output before freezing — R1 confirmed `MARTA` only).

**Documented divergence** (FR-018): after this feature a city's telemetry value differs from
its slug in both token and casing (`atlanta` vs `MARTA`). Intentional, per FR-016.

---

## C5. Config parity

Both `appsettings.json` `Cities[]` arrays MUST contain the same set of `Name` values.

**Failure mode without it**: worker publishes to `atlanta`, WebAPI serves `marta` — client
connects, receives nothing, no error (FR-008, SC-003).

**Requirement**: an automated check fails the build/test run on mismatch (FR-006, SC-007).
Compare the **set of `Name` values**, not whole-file equality — the two files legitimately
differ elsewhere.

---

## C6. Future cities

`discover-transit-city` mints slugs autonomously; it MUST apply C1 (FR-003). Its `SKILL.md`
and `add-transit-city`'s must state the rule and use city-named examples.

Compat-report filenames under `docs/city-compat/` are **not** city slugs — they are agency
documents (`rtd.md`, `septa.md`) and are out of scope.
