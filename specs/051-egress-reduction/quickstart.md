# Quickstart: Verifying Egress Reduction (051)

Phase-by-phase verification. Local run: Aspire AppHost or WebAPI+Worker directly; production checks use the telemetry MCP bridge and Log Analytics.

## Phase 0 — Measurement & observability

1. **Column emits**: run the stack ~2 minutes, then query via the telemetry bridge:
   `dataset=telemetry`, filter `event_type = 'PerCityCycle' and batch_wire_bytes > 0` — rows appear with plausible sizes (mid-size city ≈ 40–80 KB; NYMTA ≈ hundreds of KB). Verify a feed-gap tick has `batch_wire_bytes` NULL, not 0.
2. **Validator sync**: MCP query filtering on `batch_wire_bytes` is accepted; `go test ./...` in `tools/telemetry-mcp` passes (new allow-list vectors).
3. **FullCycle sum**: one tick's FullCycle `batch_wire_bytes` equals the sum of its PerCityCycle rows.
4. **Log Analytics** (after `bicep` deploy): KQL over the container-apps console-log table returns Worker log lines. Before this feature: nothing.
5. **SWA Standard**: portal shows Standard tier.
6. **Record the baseline**: capture ≥3 days of per-city daily sums — this is the denominator for Phases 1–3. Note NYMTA's share (answers the doc's open question 2).

## Phase 1 — Compression + caching

1. `curl -H "Accept-Encoding: br" -i https://<api>/gtfs/<all-shapes-route>?city=marta` → `Content-Encoding: br`, `ETag`, `Cache-Control: public, max-age=3600`; compressed size ≤30% of identity (compare with no Accept-Encoding).
2. Re-request with `If-None-Match: <etag>` → `304`, empty body.
3. Browser: cold load, DevTools Network — route-shapes transfer dramatically smaller; reload → served from cache or 304. Map renders identically (spot-check route count/colors).
4. SignalR unaffected: websocket frames uncompressed, live vehicles still animate.
5. Restart-with-refresh: after the loader repopulates, ETag changes and revalidation returns 200 + new body.

## Phase 2 — Hidden-tab pause

1. **Muted + hidden pauses**: mute audio, open DevTools WS frame view, hide the tab (switch tabs, don't close) → `LeaveCity` invocation visible; no further `ReceiveBatch` frames while hidden.
2. **Resume catches up**: after ≥1 min hidden, show the tab → `JoinCity` + immediate snapshot replay; vehicles appear at current positions with NO sweep-across-the-map animation; stale vehicles idle. Next live batch within ~10 s.
3. **Unmuted keeps streaming**: unmute, hide tab → `ReceiveBatch` frames keep arriving; soundscape continues audibly in the background tab.
4. **Mute while hidden**: with tab hidden and audio playing, trigger mute (e.g. second window/device on same session is out of scope — use the settings toggle just before hiding, then verify the leave happens on the mute event path via logs).
5. **Reconnect respect**: while paused, kill/restore the network → on reconnect the client does NOT rejoin until made visible.
6. **Rapid toggling**: flip visibility ~10× fast → final state correct, no duplicate `ReceiveBatch` handlers (frame count per tick stays 1 after settling).

## Phase 3 — Wire slimming (after ≥3 days of Phase 0 baseline)

1. **Unit gates**: `dotnet test` — Shared round-trip/size vectors (≥35% batch reduction proxy), Worker emit-rule tests (first-seen / steady / route-change / unknown-category / stale), WebAPI cache tests unmodified and green (`LastBatchCacheCrossingExclusionTests` untouched).
2. **Local end-to-end**: run stack, verify map behavior indistinguishable: animation smoothness, categories (rail vs bus dot styling), audio triggering, join snapshot (hard-reload mid-session → vehicles snap into place, no motion replay).
3. **Version gate**: run an old client bundle (pre-change commit) against the new server → join fails with a logged `HubException`, map stays empty, no garbled rendering. New client vs old server: same clean failure.
4. **Deploy order**: server+worker container → SWA client → same revision cherry-picked/merged to `deploy/marta-jazz`.
5. **Measure the win (SC-004/SC-005)**: compare per-city `batch_wire_bytes` daily sums pre/post — expect ≥35% per-vehicle reduction, ~38.7% predicted (normalize by `vehicles_processed`; baseline ~68 B/vehicle). Then compare Azure egress cost month-over-month for the ~60–75% total (with Phases 1–2 compounding).

## Acceptance traceability

| Spec SC | Verified by |
|---|---|
| SC-001 | Phase 0 steps 1–4, 6 |
| SC-002 | Phase 1 steps 1–3 |
| SC-003 | Phase 2 steps 1–2 |
| SC-004 | Phase 3 steps 1, 5 |
| SC-005 | Phase 3 step 5 (post-deploy month) |
| SC-006 | Phase 0 step 5 |
| SC-007 | Phase 1 step 3, Phase 2 step 3, Phase 3 step 2 |
