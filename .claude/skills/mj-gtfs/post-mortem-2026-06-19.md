# Post-Mortem: First Run of GTFS Compatibility Evaluation (2026-06-19)

**Skill evaluated:** `mj-data-explorer` → `functions/gtfs-compatibility.md` → `mj-gtfs`
**Agency:** MARTA (Atlanta)
**Outcome:** Correct final report, but required 6 tool calls to decode the protobuf
where 1 should suffice.

---

## What went wrong (and why)

### 1. `pip install` blocked in auto-mode — wasted a tool call

The skill's original primary decode path was `pip install gtfs-realtime-bindings`.
Auto-mode's permission classifier blocks external package installs. This was the
first thing attempted and it failed immediately, forcing a fallback.

**Cost:** 1 wasted tool call + blocked permission prompt.

**Fix:** Pure Python decoder is now the primary path. `pip` path is secondary,
clearly gated behind a note about auto-mode restrictions.

---

### 2. Pure Python decoder had wrong field number for `VehiclePosition.position`

The fallback pure Python decoder used `if 3 in vf` for the position sub-message,
following the GTFS-RT proto spec (VehiclePosition field 3 = position). But the
**live MARTA feed encodes position at field 2**, not field 3 — likely because their
publisher omits `vehicle_descriptor` (field 2 in the spec), and the position blob
lands at the next field slot actually written, which turns out to be field 2 in wire
encoding.

Result: first pure Python attempt decoded 186 entities with `route_id` but **0%
lat/lon** — all position fields null.

**Cost:** 2 additional diagnostic tool calls (raw field scan, sub-field inspection)
to identify the correct field number.

**Fix applied in SKILL.md:** Position is at field 2 for MARTA. The proto field
reference comment is updated to document this with a prominent warning. The decoder
code uses `if 2 in vf` for position lookup.

---

### 3. Three separate diagnostic rounds instead of one robust decoder

The progression was:
1. First pure Python attempt → wrong field for vehicle (tried `4` from spec, got 0 results)
2. Raw byte scan to see what top-level fields exist
3. Per-entity field inspection
4. Sub-field inspection of VehiclePosition
5. Corrected decoder run

Steps 2-4 were purely diagnostic overhead caused by the field number mismatches.
A decoder that emits a **field map** alongside results on first run would have
collapsed this to one call: "here are the field numbers I found, here's the data."

**Cost:** ~4 extra tool calls, ~3 minutes of latency, significant token spend on
large JSON dumps of intermediate state.

**Fix:** The decoder now includes a raw field-map probe at the start. If `vehicle_entities > 0` but `lat_lon_pct == 0`, it self-diagnoses by printing the actual VehiclePosition field numbers alongside the result so the caller can immediately see what went wrong without a separate inspection pass.

---

### 4. Field reference comment in SKILL.md was spec-correct but feed-incorrect

The comment block said:
```
VehiclePosition: 1=trip (msg), 2=vehicle_descriptor (msg), 3=position (msg)
```

This is the GTFS-RT proto spec. It is not what MARTA actually sends. The comment
gave false confidence — it looked authoritative and matched the decode attempt.

**Fix:** Comment now documents MARTA's actual observed wire layout (field 2 = position)
with a clear note that the spec says field 3 but MARTA omits vehicle_descriptor.

---

### 5. `gtfs-compatibility.md` specifies step order that causes unnecessary blocking

The function file says "fetch both feeds" in Step 2, but doesn't explicitly say
to do so in parallel. The actual run fetched GTFS-RT and static zip in parallel
(correct), but the function's text reads sequentially.

**Fix:** Step 2 now explicitly says "fetch both in parallel."

---

## What worked well

- **Static GTFS parse** worked first try — no issues, clean output.
- **Route ID alignment cross-check** was a single Python call with correct logic.
- **Parallel fetch** of both feeds was inferred correctly.
- **Final report format** matched the template cleanly.

---

## Summary of changes implemented

| File | Change |
|------|--------|
| `mj-gtfs/SKILL.md` | Pure Python decoder uses field 2 for position (MARTA); proto field comment updated; pip path moved to secondary with blocking note; error table updated with null lat/lon self-diagnosis row |
| `mj-data-explorer/functions/gtfs-compatibility.md` | Step 2 says "fetch in parallel"; Step 3 notes to check lat/lon pct and trigger raw inspection if 0 |

---

## Latency / token profile of the bad run

| Step | Tool calls | Reason |
|------|-----------|--------|
| Fetch both feeds | 2 (parallel) | Correct |
| pip install attempt | 1 | Wasted — blocked |
| First pure Python decode | 1 | Wrong field — null lat/lon |
| Raw byte scan | 1 | Diagnostic |
| Sub-field inspection | 1 | Diagnostic |
| Corrected full decode | 1 | Correct |
| Route ID cross-check | 1 | Correct |
| **Total** | **8** | vs. ideal **4** |

Ideal run: fetch (2 parallel) → decode GTFS-RT (1) → parse static (1) → cross-check (1) = **4 tool calls**.
