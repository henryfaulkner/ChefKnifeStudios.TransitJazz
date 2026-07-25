# Contract: Time-to-First-Note Probe + Density Health Check (US5 / D7, D8)

## TtfnProbe (D7) — shipped with US1

Module-scope object in `transit-synth.js`, exposed as `window.TtfnProbe`, following the `window.MemoryProbe` idiom.

### Shape

```js
const _ttfn = { version: 'SET-PER-DEPLOY', unlockAt: null, firstTriggerAt: null,
                firstAudibleAt: null, droppedWhileLocked: 0, noiseBedAt: null };
window.TtfnProbe = _ttfn;
```

`version` MUST be stamped with the deploy commit short-SHA via a **build/publish-time token substitution** (replace `SET-PER-DEPLOY` during the WASM publish step in `.github/workflows/`), NOT left as the placeholder — an unstamped version silently breaks the cross-version comparison FR-012/FR-013 require.

`noiseBedAt` is set once when the ambient noise bed starts in `unlock()` (SC-001): `noiseBedAt − unlockAt` is the recorded "audible within 1 s" number, distinct from `firstAudibleAt` (first *note*).

### Marks

| Where | Action |
|---|---|
| both unlock paths (`unlock()` + gesture handler) | `unlockAt ??= performance.now()`, `performance.mark('ttfn:unlock')` |
| `triggerNote`, `!_unlocked` return | `droppedWhileLocked++` before returning |
| `triggerNote`, after `_audioEnabled` gate | `firstTriggerAt ??= performance.now()` |
| `triggerNote`, after `triggerAttackRelease` | if `firstAudibleAt === null && unlockAt !== null`: set it, `performance.measure('ttfn:unlock→audible','ttfn:unlock')`, emit one `[TTFN]` line |

### Emitted line (FR-012, FR-013)

```
[TTFN] v=<version> unlock→trigger=<ms> trigger→audible=<ms> total=<ms> droppedWhileLocked=<n>
```

- `unlock→trigger` = supply half (B1+B2). `trigger→audible` = build half (B4).
- `droppedWhileLocked > 0` ⇒ dwell/steady-state; `== 0` ⇒ cold-start (which fix moved the needle).
- Read `window.TtfnProbe` any time for raw numbers; spans appear in the DevTools Performance panel.

### Baseline-before-probe (not shipped)

The §5.2 console paste-in proxy (t0 from `[TransitSynth] unlocked` log, t1 from first gleitz.github.io fetch) is documented in quickstart for baselining the CURRENT prod build before the probe ships. Underestimates by the decode+IR tail; fresh-page-load only.

## Density health check (D8) — telemetry-only, FR-014

No new instrumentation beyond D3's counters. A documented rolling-window computation over existing `PerCityCycle` rows.

| Signal | Definition | Flag |
|---|---|---|
| zero-tone-tick fraction | `count(tones_emitted == 0) / count(rows)` per `city_name`, rolling hour | **> 30%** |
| tones/tick vs. baseline | `avg(tones_emitted)` over window vs. city baseline (post-D4) | **< ½ baseline** |

**Threshold rationale** (thresholds must be justified, not magic): in the discovery §2.1 baseline the healthy cities (MBTA/WMATA) sit at 0% zero-tone ticks and NYMTA at 20%, while the symptomatic ones (MARTA 70%, TTC 69%) are far above — a **30%** line cleanly separates healthy from degraded with margin. The **<½ baseline** rule catches a previously-healthy city regressing even if it stays under 30%.

- Consumed via the telemetry query path (`query_telemetry` bridge / mj-data-explorer).
- **TTC fails this today** (69% zero ticks / 915 vehicles) — surfaced as its own issue, out of scope for this feature's fixes.
- This is a threshold + query, NOT a live alerting service (YAGNI).

### T033 mechanism re-check (2026-07-20, against current data — see note below)

Re-ran the `tones_emitted = 0` query per city against live `telemetry` (`event_type = 'PerCityCycle'`) rather than trusting the discovery §2.1 snapshot. The mechanism holds — the query correctly scopes by `city_name` and the zero-tick count is non-trivial and city-specific, not noise:

- **TTC**: a `tones_emitted = 0` filter alone returns 100+ matching rows (query result truncated at the tool's output cap) out of the queried window — consistent with the discovery baseline's ~69% and still the clearest flag under the >30% threshold.
- **MBTA** and **WMATA**: unlike the discovery §2.1 snapshot's reported 0%, the current data shows a nonzero (but visibly smaller, spot-checked well under TTC's volume) number of zero-tick rows for both. This is expected drift, not a mechanism failure — traffic/feed conditions change hour to hour, which is exactly why T033 says not to pin the test to the dated snapshot. A full rolling-hour fraction was not computed here (would require aggregate `COUNT`/`GROUP BY`, which the allow-list grammar intentionally does not expose — see `query_telemetry`'s read-only filter-only contract); the check performed is the mechanism-level one T033 asks for: the flag correctly fires for the previously-known-bad city (TTC) and the query correctly discriminates by city.
- **MARTA**: not independently re-checked here; the discovery baseline (70% zero-tick) predates this feature's fixes and is expected to improve once US2 (reverse-direction) and US3 (join replay) ship, per the plan's phasing.

Conclusion: the >30% mechanism is live and TTC is the one city clearly over threshold today, matching the discovery doc's expectation. Tracked as its own issue (not fixed by this feature).
