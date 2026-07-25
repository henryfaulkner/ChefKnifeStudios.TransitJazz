# Quickstart: Verifying the MapLibre Migration

**Feature**: 007-maplibre-migration | **Date**: 2026-05-18

This is the verification protocol for the migration. Run it after all renames, deletions, and edits are in place, *before* opening the PR. Each step maps to one of the five success criteria in `spec.md`.

---

## Pre-verification checklist

Before running the verification, confirm:

- [ ] All five §A renames in `data-model.md` are complete.
- [ ] All eleven §B deletions in `data-model.md` are complete.
- [ ] The order rule was followed: the old `Map.razor*` files and old `vehicle-animator.js` were deleted *before* the renames overwrote those paths.
- [ ] All eight §C single-line/block edits in `data-model.md` are applied.
- [ ] The constitution amendment in `contracts/constitution-amendment.md` is applied in full (5 sections).

---

## Verification step 1 — Build cleanly (SC-004)

```powershell
dotnet build C:\Projects\ChefKnifeStudios.TransitJazz\ChefKnifeStudios.TransitJazz.sln
```

**Pass criteria**: `0 Error(s)`. Warning count MUST NOT exceed the pre-migration baseline (POC-era warnings around nullable annotations are pre-existing and acceptable, but no *new* warnings should appear in the migrated files).

If errors: most likely the rename missed a `MapLibre` → `Map` substitution inside one of the Helper file's `JSInterop` calls, or a stale reference to `ChefMapLibre` survives in a renamed file. Re-grep.

---

## Verification step 2 — Production page renders (SC-001, SC-002)

1. Start the full local stack (AppHost + WebAPI + Worker).
2. Wait for the log line `GtfsStaticLoader: loaded {Count} route shapes.` to appear in the WebAPI console.
3. Open Chrome (or Edge), open DevTools → Network panel, check **Disable cache**.
4. Navigate to `https://localhost:5xxxx/transit-map`.
5. Confirm:
   - MapTiler vector tiles render (Atlanta street map visible).
   - Within ~10 seconds of live SignalR data flowing, vehicle markers appear and animate.
   - Route lines render in their MARTA colors.
6. Filter the Network panel by `atlas.microsoft.com`. **Result MUST be zero requests.** (SC-001)
7. Click a vehicle marker — verify the `[Map] Vehicle marker clicked: …` line appears in the browser console (it was renamed from `[MapLibreTest]` in the migration).
8. Click an empty area of the map — verify the `[Map] Map body clicked` line appears.

---

## Verification step 3 — Dead-code search (SC-003)

Run each grep from the repo root. Each MUST return zero hits in production-path files (`src/`, `.specify/`, root config). Hits in `specs/` (specification artifacts) and git history are expected and ignored.

```powershell
# Azure Maps CDN
Get-ChildItem -Recurse -Path src,.specify -Include *.cs,*.razor,*.js,*.css,*.html,*.json,*.md | Select-String 'atlas\.microsoft\.com'

# Old Azure Maps configuration key
Get-ChildItem -Recurse -Path src,.specify -Include *.cs,*.razor,*.js,*.json,*.md | Select-String 'mapAccClientId'

# Old POC page name
Get-ChildItem -Recurse -Path src,.specify -Include *.cs,*.razor,*.js,*.json,*.md | Select-String 'MapLibreTest'

# Azure Maps SDK type references — these distinguish "old ChefMap" from "new (renamed) ChefMap"
Get-ChildItem -Recurse -Path src,.specify -Include *.cs,*.razor,*.js,*.md | Select-String 'atlas\.Map|atlas\.data\.Feature|atlas\.source\.DataSource|getShapeById'

# Old POC namespace
Get-ChildItem -Recurse -Path src,.specify -Include *.cs,*.razor,*.js,*.md | Select-String 'ChefMapLibre|MapLibreAnimator'
```

If any of these return hits in a `src/` file, that file is incomplete. Fix and re-run.

---

## Verification step 4 — Constitution amendment landed (SC-005)

```powershell
Get-Content .specify/memory/constitution.md | Select-String 'Azure Maps'
```

**Pass criteria**: Zero hits. (Note: the *Sync Impact Report* block in the new constitution refers to "auth model changed from Azure Maps Auth Function to MapTiler…" — that single reference in the history block is acceptable; the grep should be re-run with `-NotMatch 'auth model changed'` to confirm no operative references remain. Operative meaning: no current architecture description should claim the app uses Azure Maps.)

Re-grep:

```powershell
Get-Content .specify/memory/constitution.md | Select-String 'URL-restricted public API key'
```

**Pass criteria**: At least one hit, inside Principle II.

```powershell
Get-Content .specify/memory/constitution.md | Select-String '\*\*Version\*\*: 3\.0\.0'
```

**Pass criteria**: Exactly one hit, in the footer.

---

## Verification step 5 — Manual smoke (full user flow)

Open `https://localhost:5xxxx/` (the site root or whichever the home page is) and click through any navigation that leads to `/transit-map`. Confirm:

- The site index does not show a link to `/maplibre-test` (that page is deleted).
- The site index does not show a link to a former `/azure-maps-test` page either, if one existed.
- The `/transit-map` page is reachable from production navigation as before.

---

## Success criteria mapping

| Success criterion | Verification step |
|---|---|
| SC-001 (zero Azure Maps CDN requests) | Step 2.6 |
| SC-002 (no animation regression) | Step 2.5 |
| SC-003 (zero dead-code references) | Step 3 (five greps) |
| SC-004 (build clean) | Step 1 |
| SC-005 (constitution updated) | Step 4 |

When all five rows pass, the migration is complete and the PR is ready.

---

## If something fails

- **Build error in `Map.razor.cs` after rename**: The class name inside the file was not updated. Open the file and verify `public partial class Map` (not `MapLibre`).
- **`ChefMap.createMap is not a function` in browser console**: The JS rename in `map-interop.js` was incomplete — `window.ChefMapLibre = {` survives somewhere. Search the file.
- **Browser shows blank grey map**: The MapTiler key in `appsettings.json` is invalid or not yet URL-restricted to localhost. Check the MapTiler console.
- **Server fails to start with `Azure.Identity` resolution error**: The `using Azure.Identity;` line survives somewhere — `MapsEndpoints.cs` was edited rather than fully deleted. Delete the file.
