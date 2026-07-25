<!-- SPECKIT START -->
For additional context about technologies to be used, project structure,
shell commands, and other important information, read the most recent
feature plan at specs/049-backfill-texture-selector/plan.md

049-backfill-texture-selector exposes the soundscape's continuous background
"backfill" filler as a user-selectable choice. Today `transit-synth.js` runs ONE
fixed layer — a pink-noise bed on the master bus gated by the global audio mute
(`getMasterBus`, the `Tone.Noise('pink')` at ~-38dB). This feature generalizes that
single node into a **swappable backfill layer** with two mutually-exclusive states:
**Noise** (today's bed, the default — byte-for-byte unchanged) and **Percussion** (a
new sparse, humanized lo-fi kit — `MembraneSynth` kick + `MetalSynth` rim on a slow
`Tone.Loop`, the app's FIRST use of `Tone.Transport`, feeding the same master
compressor so it inherits the master glue/softening). Engine changes: new
`_backfillMode`/`_percussion` state, a `setBackfillTexture(mode)` export shaped like
`setAudioEnabled`, a single `_applyBackfillLayer()` choke point that reconciles
`_audioEnabled × _backfillMode` → exactly one running layer (so mute stops BOTH and
unmute restarts WHICHEVER is selected — the two gates never drift), `buildPercussion`,
and updates to `getMasterBus`/`setAudioEnabled`/`dispose` + the export map. There is
ALWAYS a backfill — no "off"; total silence stays the separate audio mute's job. C#:
a `BackfillTexture { Noise, Percussion }` enum + `[HiddenSetting]` persisted property
on `Settings.cs` (bump `CurrentVersion` 4→5; `[HiddenSetting]` keeps it out of the
reflection-driven bool-only `SettingsBlade`), a `SetBackfillTextureAsync(string)`
interop mirroring `SetAudioEnabledAsync`, and a new `BackfillTextureFab.razor`
(`graphic_eq`, structured like `CityFab`'s MatFAB+MatMenu, wired like `AudioFab`'s
read/persist — but NO event bus, nothing else consumes the choice, YAGNI) mounted in
`MainLayout` and pushed on init beside `SetAudioEnabledAsync` in `TransitMap.razor.cs`
(~L110). Persistence reuses the existing `SettingsService` local-storage blob (per the
2026-07-25 clarify) — enum only, never live Tone.js nodes. Labels via
`IStringLocalizer<RouteFilterResources>` (EN keys only; `.es` deferred per 015/016/017).
The percussion SOUND is the real experiment: its final `PERCUSSION_*` constants are
dialed in by ear FIRST via a new Backfill audition mode added to
`tools/instrument-compat/` (which already reproduces the app's exact master bus, so the
loop auditioned as a sibling node is fidelity-accurate — no throwaway page), then
transcribed by hand (no export, matching 047). SUPERSEDES the deferred
`docs/DRUMKIT_AND_DENSITY_DESIGN_DOCUMENT.md` (its event-driven-off-transit percussion
is rejected; only its settled synth-drum voice palette §4 is carried over) — mark it
with a one-line SUPERSEDED banner at implementation time. Frontend-only; no
server/worker/shared changes. See specs/049-backfill-texture-selector/ for spec, plan,
research, data-model, the four contracts (synth-engine, settings-interop, backfill-fab,
audition-tool), and quickstart (audition-first, then D1-D10 acceptance).

048-septa-transit adds Philadelphia **SEPTA** as a live-vehicle city, per
docs/city-compat/septa.md (92/100, Drop-in). Two-part fork: (1) the live-vehicle
path is pure config — SEPTA's buses, trackless trolleys, streetcars, and the
Norristown High Speed Line (route_type=1, route_id "M1") all ride one keyless
GTFS-RT feed with 100% route_id/lat/lon coverage and a verbatim
route_id==route_short_name match, so `septa` falls into the existing `else` arm
of the Worker's city-registry factory and is served by the config-driven
GtfsRtCity — zero new classes, same as WMATA/MBTA/TTC. (2) the static-GTFS path
needs one new, narrowly-scoped capability: SEPTA's gtfs_public.zip is a
zip-of-zips (nested google_bus.zip + google_rail.zip), and GtfsStaticLoader.cs
previously only handled flat single-level zips. BuildCityShapeSetAsync gains an
additive, city-agnostic detect-root-else-unwrap-nested-zip step (prefers the
non-"rail"-named nested entry) — a no-op for every existing flat-zip city,
unit-tested for flat/nested/fallback-with-no-match cases. Regional Rail
(google_rail.zip, route_type=2) is out of scope — never unwrapped. Broad Street
Subway / Market-Frankford Line (B1/B2/B3/L1) share the same feed/ID scheme as
M1 but showed zero live vehicles in the compat report; no bespoke rail adapter
is built for them — they'll flow through the same generic path automatically
if SEPTA ever emits live positions under those IDs. Standard registration
touch-points otherwise: CityNames.Septa constant, Worker+WebAPI appsettings.json
Cities: entries (byte-identical, keyless), CityFab.razor picker button
("Philadelphia, PA"), map origin at Center City/15th & Market (39.9526,
-75.1652, not geographic centroid), AudioUnlockOverlay + InfoFab copy. See
specs/048-septa-transit/ for spec, plan, research, data-model, the three
contracts (city-config, city-picker, nested-zip-extraction), and quickstart.

047-instrument-compat builds `tools/instrument-compat/index.html`, a single
self-contained static HTML developer tool (no build step, no backend, no ties
to the TransitJazz .NET solution — same standalone-tool pattern as
`tools/telemetry-mcp/`) that lets a sound designer audition candidate
instruments for the live soundscape without running the app. Faithfully
reproduces `transit-synth.js`'s exact Tone.js v15 chain: per-voice
Sampler→Filter(1800Hz)→StereoWidener(0.4)→Volume→Reverb(1.4/0.02/0.35) into a
shared, lazily-built-once master bus (Compressor→Filter(4000Hz)→Destination
plus a continuous -38dB pink-noise bed), the verbatim C-minor-pentatonic
SCALE array + noteForPosition mapping, and the ±20ms/0.75–1.0 humanization
jitter — all pinned byte-for-byte to the app's constants (constitution
Principle VIII fidelity binding, not a new principle). Explicit per-note
anchor-URL instrument add (no base-URL auto-derive mode), an Enable-Audio
gesture-gated unlock (no autoplay, mirroring the app's iOS Safari fix), a
Density Off/Low/Medium/High synthetic-crossing scheduler (uniform-random
instrument choice + random triggerIndex/totalTriggers through the real
noteForPosition, since there are no real routes to hash), fire-time-rechecked
mute (silences in-flight notes, not just newly-scheduled ones), and
localStorage persistence of instrument specs (never live Tone.js nodes —
rebuilt/re-fetched on reload) + density/mute state. Deliberately does NOT
build: base-URL convenience mode, a scale-sweep button, or PALETTE-snippet
export — onboarding a validated instrument into the app stays a manual step.
See specs/047-instrument-compat/ for spec, plan, research, data-model, the
engine/fidelity contract, and quickstart (cello-anchor smoke test).

046-discover-transit-city builds `.claude/skills/discover-transit-city/`, a hands-free
CRON-driven orchestrator skill (zero arguments, invoked weekly by a `/schedule` cloud
routine) — NOT application code, no server/worker/client/shared files touched. Each run:
(1) selects one not-yet-evaluated NA/EU transit authority from a curated `candidates.md`
pool, falling back to open `WebSearch` once exhausted, deduping by city+authority parsed
from existing `docs/city-compat/*.md` H1s (not filename); (2) resolves ambiguous
multi-operator cities via a stated tie-break rule (largest urban-core network, unified
GTFS-RT feed preferred); (3) discovers GTFS-RT vehicle-positions / static GTFS / rail-realtime
URLs, explicitly rejecting trip-updates/alerts-only endpoints and key-gated feeds (never
attempts key acquisition); (4) evaluates compatibility by DELEGATING ENTIRELY to the
existing `mj-gtfs` skill (fetch/decode) and `mj-data-explorer`'s `gtfs-compatibility`
function (interpretation table) — no new fetch/decode/interpretation logic is written;
(5) writes exactly one `docs/city-compat/{slug}.md` using one of TWO RIGID, FIELD-BY-FIELD
FILL-IN TEMPLATES authored in `specs/046-discover-transit-city/contracts/` — 
`report-template-compatible.md` (mirrors ttc.md's shape) or `report-template-blocked.md`
(mirrors cta.md's shape, D2: a negative report is a successful run) — chosen once per run,
never blended, every numeric field either a real `mj-gtfs`-measured value or the literal
token `UNASSESSED`/`N/A` (never invented); the templates' content becomes the literal body
of the skill's `references/report-templates.md` at implementation time. (6) commits ONLY
that one file to a new `compat/{slug}` branch and opens a PR to `main` via `gh pr create`
— NEVER a direct `main` commit, NEVER a merge, NEVER an `add-transit-city` onboarding
trigger (see `contracts/pr-delivery-contract.md` for the full invariant list + degraded-path
handling when push/PR-create fails). See specs/046-discover-transit-city/ for spec, plan,
research, data-model, the four contracts (both report templates, the six-stage
orchestration contract, the PR-delivery contract), and quickstart (the required manual
SEPTA dry-run before the `/schedule` routine is ever created).

017-map-style-toggle adds ONE boolean to the existing 016 Settings Blade —
IsStreetMapEnabled (default false), [Description("SettingStreetMap")] — that
hot-switches the MapTiler basemap between LightOff (new app default, off) and
LightOn (on) with NO reload, via MapLibre map.setStyle(url). The two URLs come
from the existing MapTiler:StyleUrls config object (LightOn/LightOff/DarkOn/
DarkOff already in appsettings.Development.json; the StyleUrls block must be
ADDED to production appsettings.json — Dark variants unused here). Because
setStyle REPLACES the whole style (wiping custom sources/layers), the rewritten
ChefMap.setMapStyle (was a no-op stub) CAPTURES the vehicles / trigger-points /
route-* GeoJSON sources+layers — preserving each layer's current visibility —
then re-adds them on a one-shot map.once('style.load'), NEVER re-fetching
(Principle VII). Decoupling reuses the existing IEventNotificationService bus:
SettingsBlade.HandleSettingPressed posts a new GisSettingChangedEventArgs
{ IsStreetMapEnabled } (shape mirrors AudioSettingChangedEventArgs), consumed in
TransitMap.HandleSettingsEventReceived which resolves StyleUrls:LightOn/LightOff
from IConfiguration (fallback flat StyleUrl, then no-op so the map never blanks —
FR-013) and calls a new Map.SetBasemapStyleAsync(url) interop wrapper. Initial
load honors the persisted setting: Map.GetMapSettings (the JSInvokable
getMapSettings) injects ISettingsService and picks the LightOn/LightOff URL so
the map paints the saved style from first render (FR-009). Label via
IStringLocalizer<RouteFilterResources> (resx key SettingStreetMap; EN only, .es
deferred per 015/016). This completes the GIS/basemap toggle Principle XII
mandates but 016 cut before merge (commit 9726df0 "remove Street map setting").
Frontend-only; no server/worker/shared changes; the blade's pure-reflection
render is unchanged (still boolean-only). See specs/017-map-style-toggle/ for
plan, research, data-model, contracts (map-style-events, map-style-interop,
style-config), and quickstart.

016-settings-blade implements the constitution-mandated settings panel
(Principle XII): a gear MatFAB bottom-right opens a right-side slide-out
drawer ("blade") — slide-in 100ms, instant dismissal on ✕ / outside-click /
gear re-click (Principle XI). Structure follows docs/SETTINGS_BLADE_DESIGN_
DOCUMENT.md verbatim in pattern: a generic BladeContainer shell +
SettingsBlade that REFLECTS over a boolean Settings model (ObservableObject,
[property: Description] = a resx KEY) rendering one MatCheckbox per bool, and
a SettingsService persisting one JSON blob to local storage (sync Blazored,
key "Setting", lazy-seed defaults). Shipped settings are 3 BOOLEANS — Audio
(mute/unmute), GIS (streets basemap ↔ blank dark canvas), Checkpoint
visibility — so pure reflection holds; Language selector + Dark-Mode are
DEFERRED (XII partial, tracked; mirrors 015's deferred Spanish). Reuses the
EXISTING IEventNotificationService singleton bus (handler is synchronous
void; guard `if (e is not BladeEventArgs) return;`) for FAB→blade open/close
AND per-setting effect events (AudioSettingChanged/GisSettingChanged/
CheckpointVisibilityChanged) consumed by TransitMap → synth mute / ChefMap.
setBasemapStyle (data GeoJSON layers re-added after style.load, NEVER
re-fetched — Principle VII) / ChefMap.setCheckpointVisibility. One genuinely
new interop: outside-click.js + IOutsideClickJsInterop (lazy-RCL-module idiom
like TransitSynthJsInterop). _elementId uses cached Guid.NewGuid() (the doc's
recommended fix, not its empty-GUID quirk). All blade copy via
IStringLocalizer<RouteFilterResources>. Frontend-only; no server/worker/
shared changes. Namespace root is ChefKnifeStudios.MartaJazz under src/Client/.
See specs/016-settings-blade/ for plan, research, data-model, contracts
(settings-events, settings-service, outside-click-interop), and quickstart.

014-transit-datasets retargets the tools/telemetry-mcp/ MCP bridge (Go,
mcp-go over stdio) off the iris demo dataset and onto the three frozen
parquet datasets from feature 013 (snap/lerp/cycle). The query_telemetry
tool gains a required `dataset` arg (validated against {snap,lerp,cycle}
BEFORE the filter) and an optional `date` arg (strict ^\d{4}-\d{2}-\d{2}$,
default today UTC). The load-bearing allow-list validator is rebuilt around
each dataset's snake_case column contract with two NEW value kinds —
timestamp (compared to date strings only, e.g. observation_utc >
'2026-06-04') and bool (unquoted true/false only). `.` is removed from
identifier chars (tightening; kills dotted-path injection + dot-quoting).
Config swaps TELEMETRY_DATASET_URI → TELEMETRY_STORAGE_URI (e.g.
azure://telemetry); the runner assembles a CONSTANT source template
{StorageURI}/{dataset}/dt={date}/*.parquet — dataset/date/filter are each
validated before assembly so operator input can never redirect the source.
Default timeout raised 10s→30s. Forbidden keyword/char/URL checks and the
delegated telemetry-query-tool (AZURE_STORAGE_CONNECTION_STRING) are
UNCHANGED. All changes are in tools/telemetry-mcp/ only. See
specs/014-transit-datasets/ for plan, research, data-model, contract
(query_telemetry.tool.md accept/reject vectors), and quickstart.

013-logging-sidecar-service adds an in-process logging sidecar to the
TransitDataWorker (NOT a new deployable): data-processing code posts marker
event-args (Snap/Lerp/Cycle) onto an in-process IEventNotificationService
(server copy of Client.Core's notification pattern); a hosted LogEventWorker
drains a bounded Channel (DropWrite load-shedding, never blocks the hot path)
and a StructuredLoggingService builds parquet IN-PROCESS with Parquet.Net and
uploads one immutable part-file per dataset to Azure Blob via
DefaultAzureCredential (managed identity — NO committed account key, per
the feature-012 FR-020 security gate). Layout: telemetry/{snap|lerp|cycle}/
dt=YYYY-MM-DD/part-<utcts>.parquet, flushed every 5 min + best-effort on
shutdown, read downstream by the telemetry-query-tool (DuckDB). Column names
are a frozen snake_case contract the feature-012 allow-list consumes. All new
files under TransitDataWorker/Logging/. See specs/013-logging-sidecar-service/
for plan, research, data-model, contracts (parquet schemas + blob layout),
and quickstart.

012-telemetry-mcp-bridge is a standalone, local developer tool (NOT part
of the TransitJazz .NET app or its WASM/Docker deployment): a small Go
MCP server (github.com/mark3labs/mcp-go) that runs over stdio and exposes
a single query_telemetry tool to Claude Code. Architecture: WRAP the
existing telemetry-query-tool/ in this repo (an operator-owned Go CLI that
runs arbitrary DuckDB SQL with the Azure extension + a live storage
credential against iris.parquet in Azure Blob) via exec, passing one
fully-assembled SQL statement as argv[1]. Because that underlying tool is
NOT read-only (it'll run any DuckDB SQL — read other blobs, local files,
COPY..TO, INSTALL extensions), the feature's core is SECURITY: the source
design doc interpolated LLM text straight into the SQL string (injection
hole); this plan replaces that with a load-bearing allow-list grammar
(tokenize → parse → re-emit a canonical predicate over a fixed column
set) + a constant data-source, so input can never change the data source,
chain statements, inject comments/escapes, or reach the shell. ALSO in
scope (FR-020): telemetry-query-tool/main.go hardcodes a live Azure
AccountKey in committed source — move to env var + rotate. The new bridge
lives at tools/telemetry-mcp/ (own Go module). See
specs/012-telemetry-mcp-bridge/ for plan, research (correct mcp-go API —
the doc's main.go is NOT buildable; DuckDB threat model; the tool also
currently fails to build per build_err.txt), data-model (ValidationPolicy
grammar), contracts/query_telemetry.tool.md (accept/reject vectors), and a
9-test quickstart incl. secret remediation.
<!-- SPECKIT END -->
