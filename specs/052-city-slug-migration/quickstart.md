# Quickstart: City Slug Migration

Cutover procedure and per-city verification. Read
[`contracts/signalr-cutover.md`](./contracts/signalr-cutover.md) first — the deploy order is
load-bearing.

---

## Preconditions

- [ ] **051 Phase 3 baseline window has closed** (FR-023). Non-negotiable — migrating during it
      destroys the ≥3-day `batch_wire_bytes` baseline.
- [ ] All Tier 0/1/2 tests green on `052-city-slug-migration`.
- [ ] Access to Worker + WebAPI logs.
- [ ] `deploy/marta-jazz` ready to receive the same changes.

---

## Step 0 — Prove telemetry is decoupled (do this first)

The single most important pre-flight check. If this fails, **stop** — the rename will silently
rewrite parquet history.

```powershell
dotnet test src/Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker.Tests `
  --filter "FullyQualifiedName~Telemetry"
```

Expect green on:
- `telemetry_name_is_agency_valued_not_slug`
- `telemetry_name_values_are_frozen`
- `per_city_cycle_writes_telemetry_name_as_city_name`
- the **pre-existing** `TelemetryEventSchemaTests` asserting `city_name == "MARTA"`

> If someone "fixed" the pre-existing `"MARTA"` fixtures to `"atlanta"`, FR-016 is already
> broken. Revert that before continuing.

Confirm by inspection that `Worker.cs:103` reads `TelemetryName`, not `Name`.

---

## Step 1 — Local verification

```powershell
dotnet build ChefKnifeStudios.TransitJazz.sln
dotnet test  ChefKnifeStudios.TransitJazz.sln
```

Config parity — the check that prevents the silent group mismatch:

```powershell
dotnet test src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI.Tests `
  --filter "FullyQualifiedName~city_config"
```

Confirm no agency literal survives (expect **no** matches):

```powershell
Select-String -Path "src/Client/**/CityFab.razor" -Pattern "hash='(marta|wmata|mbta|nymta|ttc|septa|rtd)'"
Select-String -Path "src/Server/**/appsettings.json" -Pattern '"Name":\s*"(marta|wmata|mbta|nymta|ttc|septa|rtd)"'
```

Then run locally (Aspire host) and walk all seven slugs per the Step 4 checklist.

---

## Step 2 — Deploy server + worker (atomic)

These two **must** ship together — they share the group name and the config city list.

After deploy:

- [ ] Worker logs show ticks for all 7 cities under **new** slugs
- [ ] WebAPI logs show `TransitHub.JoinCityV2 ... joined group '<new-slug>'`
- [ ] Group names in join logs **exactly match** the worker's publish targets

> **Expected during this window**: users on the old client see a loud join failure. That is the
> version gate working (FR-009). Silence here would be the bug.

---

## Step 3 — Deploy client

- [ ] Fresh load of `#atlanta` joins successfully and receives vehicles
- [ ] No `JoinCity` (unversioned) invocations remain in logs
- [ ] Hard-refresh an old session — it recovers (FR-011)

Then apply the identical change to `deploy/marta-jazz` and repeat this check there.

---

## Step 4 — Per-city verification (FR-022)

Run for **all seven**. Do not sample.

| City | Slug |
|---|---|
| Atlanta | `#atlanta` |
| Washington DC | `#washington-dc` |
| Boston | `#boston` |
| New York | `#new-york-city` |
| Toronto | `#toronto` |
| Philadelphia | `#philadelphia` |
| Denver | `#denver` |

Per city:

- [ ] Map loads centered on the correct city (FR-014 — origin unchanged)
- [ ] **Vehicles appear and move** (SC-001)
- [ ] Active-vehicle count is non-zero and plausible
- [ ] **A crossing produces audio** (SC-002)
- [ ] Route shapes render
- [ ] Audio-unlock overlay shows *that city's* copy, no missing strings (SC-005)
- [ ] Info panel shows that city's copy (SC-005)
- [ ] Picker shows the current city disabled; selecting another navigates correctly
- [ ] Server log shows a join to the expected group name
- [ ] No client console errors

> **"No errors" is not sufficient evidence.** A silent group mismatch produces clean logs on
> both sides and an empty map. Only observed vehicle arrival closes SC-003.

---

## Step 5 — Post-cutover telemetry check

- [ ] New parquet rows still carry **agency** `city_name` (`MARTA`, `WMATA`, …)
- [ ] A query spanning the cutover date returns one continuous series per city — no split,
      no gap, no new distinct value (SC-006)
- [ ] `tools/telemetry-mcp` needs no change; `city_name = 'MARTA'` still validates (FR-019)

```
city_name = 'MARTA' AND observation_utc > '<cutover-date-minus-2>'
```

Expect an unbroken row count across the boundary.

---

## Step 6 — Known accepted consequences

Not bugs. Confirm they match expectations, and tell anyone watching dashboards.

| Consequence | Detail |
|---|---|
| **Old links break** | `#wmata`, `#nymta`, etc. fall through to the default city **silently** — no error, no redirect. Aliasing was explicitly declined. `#marta` lands in Atlanta only because Atlanta is the fallback default, not because it is aliased. |
| **Analytics discontinuity** | Umami shows `/marta` before and `/atlanta` after, as two paths. Not a traffic drop (research R8). |
| **Telemetry diverges from slug** | `city_name` stays `MARTA` while the slug is `atlanta` — intentional (FR-016, FR-018). |
| **Stale clients fail loudly** | Until users refresh. By design (FR-009). |

---

## Rollback

Revert all three lanes to the previous release — server+worker first, then client. Because
telemetry was never rewritten (Step 0), **rollback leaves no data artifact**: parquet history
is untouched either way, and the in-memory route-shape store rebuilds from GTFS on startup.

Partial rollback of a single lane is **not** safe: server-on-new + client-on-old is exactly the
mismatch the version gate turns into a hard failure.
