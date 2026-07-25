---
name: add-transit-city
description: Orchestration checklist for onboarding a new transit agency/city to TransitJazz (MartaJazz) — compat check, speckit spec/plan/tasks/implement, the four registration edits, map origin, and both overlay texts. Use when the user says "add a city", "onboard [agency]", "new transit city", or names a transit agency (e.g. "add Chicago CTA", "onboard BART") they want wired into the app.
---

# Add Transit City

A new city touches the same ~8 places every time. This skill is the checklist and
ordering — each step delegates to an existing skill/command rather than duplicating it.

Root namespace is `ChefKnifeStudios.MartaJazz.*` even though the repo folder says
`TransitJazz` (see `CLAUDE.md`). **Never auto-commit** — commits are the user's to make.

## Order of operations

```
1. Compat check  →  2. Fork decision  →  3. speckit spec→plan→tasks→implement
                                              │
                     (speckit-implement performs steps 4-7 per its tasks.md)
4. CityNames constant
5. Worker + WebAPI appsettings.json (parallel, must match)
6. CityFab.razor picker button + handler
   ── build ──
7. Map origin coordinate
8. AudioUnlockOverlay content
9. InfoFab content
   ── build ──
10. Live verification
```

Steps 4-6 must land (and build clean) before 7-9, because those need `CityNames.X` to
exist and the city to be reachable via the picker to test against.

## 1. Compatibility check (before any spec work)

Use the `mj-gtfs` skill to fetch and decode the agency's GTFS-RT feed, static GTFS zip,
and rail-realtime API (if it runs heavy rail). Produce a report at
`docs/city-compat/{agency-slug}.md` mirroring the existing reports (`ttc.md`, `wmata.md`,
`mbta.md`, `nymta.md`): feed health, vehicle-position field coverage, route-ID alignment
between RT `route_id` and static `route_short_name`, and a Rail section.

**Do not proceed to speckit until this report exists.** It's the input `/speckit-plan`
reasons from, and it's where the fork decision below gets made explicit.

## 2. Fork decision — config-only vs. bespoke adapter

Check `src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/Program.cs` — city
construction is a 3-way branch:

```csharp
if (string.Equals(cfg.Name, CityNames.Marta, ...))      // bespoke: rail-realtime merge
else if (string.Equals(cfg.Name, CityNames.Nymta, ...)) // bespoke: subway synthesis + 2-feed merge
else                                                      // generic GtfsRtCity — config only
    cities.Add(new GtfsRtCity(cfg, httpFactory, ...));
```

Most new cities (WMATA, MBTA, TTC) fall into the generic `else` arm — **config only, zero
new classes**. A city only needs a bespoke adapter class if it has a **separate non-GTFS-RT
rail API** to merge (MARTA-style) or needs **synthesized positions** from schedule data
because no live rail feed exists at all (NYMTA-style). Decide this from the compat report
now — it changes the scope of the plan substantially, so flag it explicitly when running
`/speckit-plan`.

## 3. SDD flow

Run the existing slash commands in order, same as `specs/043-toronto-ttc-transit/` (the
most recent real example — read its `spec.md`/`plan.md`/`research.md`/`data-model.md`/
`contracts/`/`tasks.md` as the template):

1. `/speckit-specify` — feature description references the compat report from step 1.
2. `/speckit-clarify` — only if the spec has `[NEEDS CLARIFICATION]` markers.
3. `/speckit-plan` — state the fork decision (step 2) explicitly here.
4. `/speckit-tasks`
5. `/speckit-implement` — executes the foundational edits (steps 4-6 below) plus any
   bespoke adapter code the plan called for, then runs verification tasks.

If the city is config-only, `/speckit-plan` should produce `contracts/city-config.md` and
`contracts/city-picker.md` matching TTC's, and the "implementation" tasks in the generated
`tasks.md` are mechanical edits + live verification, not new code.

## 4-6. The four registration edits

These are what `/speckit-implement` actually executes for a config-only city. Do them in
this order; steps 5a/5b are parallel (different files), 6 is independent, then build.

**a. `CityNames` constant** — `src/ChefKnifeStudios.MartaJazz.Shared/CityNames.cs`:
```csharp
public const string {Agency} = "{lowercase-slug}";
```

**b. Worker `Cities:` entry** — `src/Server/ChefKnifeStudios.MartaJazz.Server.TransitDataWorker/appsettings.json`,
appended to the `Cities:` array. Only include fields the agency actually needs (omit
`RailRealtime`/`RailRouteIdMap`/`RouteIdNormalization`/`ApiKeyEnvVar` if unused — see TTC's
entry for the minimal keyless shape, WMATA's for `RailRouteIdMap`, NYMTA's for
`RouteIdNormalization` + `BusGtfsRtUrls`):
```json
{
  "Name": "{slug}",
  "GtfsRtUrls": [ "..." ],
  "StaticZipUrls": [ "..." ],
  "EmitsTelemetry": true
}
```
Percent-encode any literal spaces in URLs (`%20`) — do not rely on the HTTP client to do it.

**c. WebAPI `Cities:` entry** — `src/Server/ChefKnifeStudios.MartaJazz.Server.WebAPI/appsettings.json`.
**Must be byte-identical** to (b) — this drives `GtfsStaticLoader` shape loading; the Worker
entry drives the live fetch. If they diverge, shapes and live vehicles disagree.

**d. `CityFab.razor` picker button** — `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/Components/FABs/CityFab.razor`.
Add a `MatListItem`/`MatButton` and a handler mirroring the existing ones (e.g.
`HandleTtcClicked`):
```razor
<MatListItem>
    <MatButton Label="{City, ST}" Mini="true" @onclick="Handle{Agency}Clicked" Disabled="@(CurrentCity == CityNames.{Agency})" />
</MatListItem>
```
```csharp
async Task Handle{Agency}Clicked()
{
    await JS.InvokeVoidAsync("eval", $"location.hash='{slug}';location.reload()");
}
```
Inline label, matching the four existing buttons — `CityFab.razor` isn't resx-localized
yet (pre-existing debt, not this task's job to fix).

**Build after (a)-(d)**: `dotnet build src\ChefKnifeStudios.TransitJazz.sln` — expect 0
errors, no *new* warnings (the solution currently has ~147 pre-existing nullable-context
warnings unrelated to city onboarding).

## 7. Map origin coordinate

`src/Client/ChefKnifeStudios.MartaJazz.Client.WebApp/Pages/TransitMap.razor.cs` —
`_cityCenter` dictionary (single source of truth for initial camera position):
```csharp
[CityNames.{Agency}] = (lat, lon),
```
Use the city's downtown/transit-dense core, not geographic centroid (e.g. TTC uses
43.6532, -79.3832 — the King/Queen/Yonge core where the streetcar network concentrates,
not Toronto's geographic center). Same zoom defaults apply to every city (`_isMobile ? 6 : 9.5`).

## 8. AudioUnlockOverlay content

Invoke the `create-audio-overlay-paragraphs` skill with the new city as the argument. It
writes the header + 3 paragraphs into
`src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/Resources/RouteFilterResources.resx`
as `{Prefix}Header`/`{Prefix}Paragraph1`/`{Prefix}Paragraph2`/`{Prefix}Paragraph3`, and — if
it hasn't already — must wire the prefix into
`src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/Components/AudioUnlockOverlay.razor`'s
`OnInitialized` switch:
```csharp
CityNames.{Agency} => "{Prefix}AudioOverlay",
```
Requires `CityNames.{Agency}` to already exist (step 4a) and the resx switch statement to
compile against it — do this after the foundational build in step 6, not before.

## 9. InfoFab content

Shorter, plain-text companion to step 8 — one resx key, no skill invocation needed (it's a
single templated sentence, not creative writing). Add to `RouteFilterResources.resx`:
```xml
<data name="{Prefix}OverlayParagraph1" xml:space="preserve">
  <value>Every dot is a real {AGENCY} vehicle, pulled live and turned into sound. {vehicle types, e.g. "Buses and streetcars"} move through {City} in real time, and the map plays what they're doing right now.</value>
</data>
```
Wire it into `src/Client/ChefKnifeStudios.MartaJazz.Client.Shared/Components/FABs/InfoFab.razor`'s
`OnInitialized` switch:
```csharp
CityNames.{Agency} => "{Prefix}Overlay",
```
Note the prefix here has **no** `AudioOverlay` suffix — `InfoFab` and `AudioUnlockOverlay`
use sibling-but-distinct prefix families (`TtcOverlay*` vs `TtcAudioOverlay*`); don't
conflate them.

**Build again** after steps 7-9: same `dotnet build` command, 0 errors expected.

## 10. Live verification

Follow `specs/{feature}/quickstart.md` if `/speckit-implement` generated one (it should, by
the 043 pattern) — feed reachability, shapes loading, live vehicles rendering/moving on
real streets over several poll cycles, route-match counters, picker behavior, audio
overlay text rendering (check the bolded `<strong>` phrases actually render bold, not as
literal tags — a common resx-escaping mistake), and a regression pass confirming existing
cities are unaffected. This generally needs a human in the browser for the audio/visual
checks; offer to start the app and let the user drive verification rather than claiming it
works unobserved.
