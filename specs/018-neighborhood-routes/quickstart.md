# Quickstart: Neighborhood Routes

How to run the tool and verify it satisfies the spec. There is no automated test suite (research D12) — verification is this checklist against the committed output.

## 1. Prerequisites

- Python 3.10+ (3.12 on the dev machine).
- The source GeoJSON `Official_Neighborhoods_with_Current_Demographic_Data_(2024).geojson` (City of Atlanta open data, 2024). Default location: `~/Downloads/`.
- Network access to the MJ API.

## 2. Install dependencies

```powershell
cd tools/neighborhood-routes
python -m venv .venv ; .\.venv\Scripts\Activate.ps1
pip install -r requirements.txt   # shapely>=2.0, requests>=2.28
```

## 3. Run

```powershell
# Defaults (GeoJSON in ~/Downloads, dev API, output beside the script):
python generate.py

# Or explicit:
python generate.py --geojson "C:\path\to\Official_Neighborhoods...geojson" --api "https://marta-jazz-dev-ca-server.jollytree-dd5ca774.eastus2.azurecontainerapps.io" --out-dir .
```

Expected: a stdout summary and two new files — `neighborhood_routes.json`, `neighborhood_routes_full.json`.

## 4. Verify (maps to Success Criteria)

| # | Check | Maps to |
|---|-------|---------|
| 1 | Summary prints total processed, count with ≥1 route, the 0-route names list, and total unique routes. | FR-013 |
| 2 | Lean file `neighborhoods` length == total neighborhoods in the GeoJSON (none dropped). | SC-001 |
| 3 | Lean `neighborhoods` is sorted ascending by `name`. | FR-006 |
| 4 | Pick a known neighborhood (e.g. *Ridgewood Heights*): its `routes` look plausible; a downtown neighborhood has many routes, an edge/rural one may have `[]`. | US1 |
| 5 | Every 0-route neighborhood named in stdout has `"routes": []` in the lean file. | SC-002 |
| 6 | Spot-check rounding: percentages have ≤1 decimal; `population`/`medianHouseholdIncome` are integers or `null`. | SC-007 / FR-007 |
| 7 | A neighborhood with a missing demographic shows `null` (not `0`) for that field. | FR-011 |
| 8 | Round-trip: take a lean `objectId`, look up `full["neighborhoods"][str(objectId)]` — record exists, its `OBJECTID_1` matches. | SC-003 / FR-012 |
| 9 | Neither file contains geometry/coordinates. | FR-009 |
| 10 | Both files have `generatedAt` (UTC) and `sourceGeoJson`. | FR-010 |
| 11 | Commute-field sanity: confirm `transitCommutePercent`/`carAlonePercent`/`workFromHomePercent` for 2–3 known neighborhoods look right (research D7 caveat). | D7 |

## 5. API-failure check (FR-014)

```powershell
python generate.py --api "https://invalid.example.invalid"
```

Expected: clear stderr error, **non-zero exit**, and **no output files written/overwritten**. (SC-004)

## 6. Skill consumers (after files committed)

- **mj-data-explorer**: ask *"which routes serve Vine City?"* / *"which neighborhoods does route 39 pass through?"* — it reads `neighborhood_routes.json` directly and answers without re-running the tool. It consults the full file only when you explicitly ask for detailed demographics on one neighborhood, looking up a single `objectId`. (FR-016, US3)
- **create-neighborhood-blurb**: give it a neighborhood name or `objectId`; it reads the lean entry and uses the route/transit/income/NPU/density signals as input to the blurb prose. (FR-017, US3 — see research D11 on how this layers onto the skill's existing sonic voice.)

## 7. Re-generation

Re-run `generate.py` only when MARTA GTFS route shapes change or the City updates the GeoJSON. Manual — no scheduled job (FR-018, design doc §8). Commit the regenerated JSON files.
