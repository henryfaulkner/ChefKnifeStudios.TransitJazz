# Client Experience Capture and Intake Contract v1

**Status**: Proposed. [Spec](../spec.md) | [Data model](../data-model.md) | [Source design](../../../docs/CLIENT_TOAST_OBSERVABILITY_DESIGN_PLAN.md).

## Boundary

The client captures only current-scope, owner-accepted outcomes. The reporter is passive and independent of notification presentation. Shared wire DTOs and fixed string codes belong in `src/ChefKnifeStudios.TransitJazz.Shared`; Core can report through a passive interface without depending on Client.Shared. The server never accepts browser-provided environment or a client secret as proof of authenticity. The browser must not submit raw settings, route/vehicle IDs, coordinates, URLs, tokens, exception text, or rendered toast copy.

Version 1 event kinds: `session_started`, `context_changed`, `attempt_started`, `attempt_finished`, `capability_changed`, `observation_window`, `toast_presented`, `runtime_failed`. Every event carries `schemaVersion: 1`, `measurementVersion: 1`, fixed `eventKind`, immutable UUID `eventId`, UUID `pageSessionId`, nullable UUID `cityActivationId` before city activation, increasing `sequence`, UTC `occurredAtUtc`, bounded nonnegative `pageElapsedMs`, allowlisted `citySlug` or `unknown`, and bounded `clientRelease` or `unknown`. Per-kind payload fields are required/forbidden by a closed schema; no arbitrary attributes.

Use stable string codes for journey, capability, action, outcome, reason, state, and the current notification issue catalog. Client and server catalog coverage must include every owner outcome and all 26 existing issue codes. Unknown future codes require a compatible server rollout before a client emits them. A changed eligibility/deadline definition increments `measurementVersion`.

## Example batch

The example illustrates shape, not the complete closed catalog. UUIDs and time values are illustrative.

```json
{
  "schemaVersion": 1,
  "events": [
    {
      "schemaVersion": 1,
      "measurementVersion": 1,
      "eventKind": "attempt_started",
      "eventId": "86a0cbba-c21c-4fcb-8ae9-a7dfa935843b",
      "pageSessionId": "72f2cb1e-7dad-4704-a467-c774f32ca70e",
      "cityActivationId": "9582011a-6bf2-4559-9601-476391db0ab6",
      "sequence": 2,
      "occurredAtUtc": "2026-11-08T15:00:00Z",
      "pageElapsedMs": 1200,
      "citySlug": "marta",
      "clientRelease": "example-release",
      "attemptId": "4cf4d19e-5829-47fb-b78a-69f66135a906",
      "journey": "city_map_start",
      "eligible": true,
      "deadlineForegroundMs": 60000
    }
  ],
  "lostCounts": []
}
```

## Kind rules

| Kind | Required data | Acceptance rule |
| --- | --- | --- |
| `session_started` | Page ID, bootstrap start offset | Once per page; bootstrap and application reuse the same ID/event. |
| `context_changed` | City/visibility/requested-state snapshot and fixed exclusion reasons | Only accepted transitions; not a generic clickstream. |
| `attempt_started` | Attempt ID, journey/action, eligibility, deadline, start offset; optional parent action ID | One accepted user attempt. Internal retries reuse it. |
| `attempt_finished` | Same ID/scope/code, one terminal result, safe reason, foreground/wall elapsed | At most one logical terminal result; arrival before start is retained for reconciliation. |
| `capability_changed` | Capability, prior/current state, reason, episode ID, owner generation; initial-snapshot flag | Unchanged reports are suppressed; only verified evidence establishes recovery. |
| `observation_window` | Sequence, monotonic bounds, observed/gap/excluded durations and state partitions | No overlapping valid windows. Capability duration partitions equal the window's observed duration. |
| `toast_presented` | Local presentation ID, displayed severity/summary kind, capability and issue/explained arrays, safe resource keys, optional known episode links | Emit after current-scope accepted presentation; one combined toast is one event. |
| `runtime_failed` | Fixed fatal category and activation | Once per category per activation; arbitrary browser errors are not fatal evidence. |

`attempt_finished` results: `succeeded`, `failed`, `deadline_exceeded`, `cancelled`, `superseded`. Capabilities: routes, map, live delivery, audio. Health states: `pending`, `healthy`, `degraded`, `unavailable`, `unknown`, `not_requested`. Action effects and requested persistence get distinct attempts under one parent action ID; raw selected values stay local. A verified `still_playing` stop outcome is urgent even with no toast.

For toast capture, snapshot page/city, accepted UTC time, local presentation ID, displayed severity, and copies of all summary arrays at the coordinator commit point. Reporting executes outside the coordinator lock. A failed presenter or suppressed notification produces no `toast_presented`, while owner telemetry remains eligible.

## HTTP intake

`POST /api/client-experience/batch` is anonymous and uses the configured application API base URL. The full path is intentional: current endpoint groups map at root, and deployed edge/proxy routing must be verified before client enablement. All responses use `Cache-Control: no-store`; neither logs nor errors echo payload content.

| Result | Meaning | Client action |
| --- | --- | --- |
| `200` | Valid envelope processed; body lists `acknowledgedEventIds` and `rejectedEvents` (`eventId`, fixed safe reason). IDs acknowledged only after durable insert or confirmed prior insert. | Delete acknowledged and permanently rejected IDs only. Keep all others. |
| `400` | Malformed envelope or invalid batch shape. | Quarantine envelope; do not spin. |
| `413` | Request body too large, including unknown-length bodies. | Split bounded batch; discard single oversize member with loss count. |
| `429` | Source/global rate limit with `Retry-After`. | Retry after the indicated delay. |
| `503` | Persistence unavailable or feature disabled for intake. No success acknowledgement. | Retain IDs and retry with backoff. |

Bound bytes while reading regardless of `Content-Length`, batch member count, UUIDs, enum/catalog membership, string and array lengths, nonnegative counter/duration fields, and observation partition sums. Validate members independently after the envelope passes. `200` can acknowledge duplicates and reject invalid members in the same response. A failed transaction acknowledges none. Valid individual events do not wait for causal predecessors; out-of-order starts/results are reconciled in reviewed views. Conflicting logical results/windows are quarantined from derived metrics and counted as quality errors.

Initial browser transport budget: 1,000 queued events or 4 MiB, seven-day age limit, 20 events or 64 KiB per batch, ten-second request timeout, immediate first send and up to two-second coalescing. Retriable failure backoff begins at two seconds with jitter, caps at five minutes, and honors `Retry-After`. One sender runs per page; atomic per-event browser writes/deletes permit multiple tabs. Unavailable durable browser storage falls back to bounded memory. Age/capacity loss evicts oldest events without favoring failures over successes and reports bounded kind-level loss counts when possible. No unload request is required for correctness.

## Server and configuration obligations

- Telemetry defaults disabled. If enabled without `ConnectionStrings:TransitJazzDB`, startup validation fails. Migrate the database and deploy a compatible server catalog before enabling emitting clients.
- Enforce a dedicated per-source rate policy, a global ceiling, bounded persistence concurrency, and request timeout. Initial rate proposal: 30 batches/minute/source with a 10-request burst and 100 batches/second per replica globally; tune after successful-session load and shared-NAT testing. Trust forwarded IP only from configured proxies.
- Make the existing Stats rejection callback endpoint-aware so client-experience 429 responses do not count as Stats requests. Provide a dedicated fixed-label collection meter and a bounded database/ingest probe without inserting fake user events.
- Verify production web origin CORS/proxy behavior and early-bootstrap API configuration. The repository currently configures localhost CORS origins only.
- A dedicated synthetic intake path accepts only check-runner-authenticated submissions, assigns the synthetic environment server-side, and never trusts a payload flag. The scheduled browser runner redirects its own client-experience requests there with a runner-held credential; that credential is absent from the WebAssembly bundle and source files. Synthetic events are excluded from real-session denominators.
