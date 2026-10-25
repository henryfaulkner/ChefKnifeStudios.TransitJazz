# Data Model: Current Capability Incidents

Browser-memory only. Proposed names describe contracts, not required class proliferation. No EF model, persisted notification record, server DTO or schema migration.

## Scope and owner outcomes

`NotificationScope`: opaque page generation plus configured city. Full city reload starts fresh; replacing/disposal invalidates old tokens before asynchronous completions. Current route selection and explicit actions use a monotonic owner operation generation. Operation/connection/attempt numbers never enter incident keys.

`Capability`: fixed `Live`, `Routes`, `Map`, `Audio`, `Settings`, `Navigation`.

`CapabilityOutcome`: scope, owner generation, fixed reason, requested/desired flag, pending/healthy/failed/paused/cancelled state, terminal evidence, usable fallback/partial state and local explanation ownership. Browser wrappers expose only bounded reason values, never arbitrary text/URLs. Owners hold detailed diagnostics locally. Actual outcome and presentation decision are separate.

Live additionally tracks connection generation, transport state and confirmed current-city membership. Routes distinguish Pending/Usable/Unavailable/Cancelled with validated current-city data. Audio distinguishes locked, enabling, running, muted/continued-without-audio, suspended and required-support failure; unlock attempt ID prevents double completion. Map tracks instance/style generation, required layers, last valid render evidence and failure span. Settings stores confirmed snapshot, requested revision and separate apply/save results.

## Incident

| Field | Meaning / invariant |
| --- | --- |
| Code | Fixed enum from decision inventory/policy table; no dynamic exception or route keys. |
| Capability | One of six values; determines aggregation and recovery ownership. |
| Scope / generation | Current page/city and applicable owner operation token. Stale input discarded. |
| Severity | Warning or Error; errors require requested capability/operation unavailable. |
| Active | Observed current failure; repeated reports merge without restarting deadlines. |
| FirstEvidence / EligibleAt | Monotonic timestamps; nullable until evidence/grace satisfied. |
| Communicated | Included or already-explained/suppressed impact has been conveyed. |
| ExplainedBy | Optional fixed parent/common-cause relationship, only on confirmed evidence. |
| Escalated | At most one warning-to-error escalation per incident lifecycle. |
| HealthySince | Starts only after actual capability-specific recovery; reset by flapping/pause without health proof. |
| LocalOwnership | Adequate existing local explanation excludes candidate before burst/summary. |

Bound: at most one record per known code in the current scope, plus six capability recovery latches. Superseding operation replaces/discards old record but cannot bypass capability suppression of an already communicated unresolved incident. Capabilities distinguish a newly actionable impact from duplicate symptoms. No error history or presentation-object queue.

## Coordinator state

Current scope, bounded incidents, six health/suppression latches, visible/desired state, presenter registration, modal/blocking-overlay count or set of fixed IDs, fixed BurstEndsAt (nullable), LastPresentationAt, current presentation ID/covered fixed-code set and one scheduled next deadline. Use monotonic TimeProvider timestamps; clock/calendar changes do not change policy. Serialize mutation/evaluation, then marshal renderer through registered layout delegate.

Continuous visible grace resets when hidden or desired delivery is false. Foreground reassesses live/audio and starts a fresh 10-second recovery grace unless terminal evidence exists. Healthy re-arm requires 30 continuous seconds with confirmed healthy capability; intentional pause is neutral, never proof of recovery. On pause/unknown state conservatively reset healthy accumulation. Display time counts visible reading time and pauses hidden/hover/focus independently of incident grace.

## Presentation descriptor and adapter state

`PresentationCandidate`: fresh highest severity, safe resource message/title keys and bounded capability names, included/explained fixed codes, scope, presentation ID and 8/12-second duration. It is constructed at commit, never durably queued. At most two capability names; more uses general app wording; no request counts.

ToastService retains at most one `MatToast` handle and one ID, not incident ownership. NotificationHost owns the native close, atomic announcement content, focus return target and timer attachment. Successful add commits communicated status; presenter failure logs once and does not mark unseen candidates communicated or loop retries. Future host re-registration may reassess current state. Dismissal removes display only.

## Transitions

| From / event | Result |
| --- | --- |
| Healthy -> qualifying failure | New active incident, evidence/grace eligibility. |
| Active repeat | Merge bounded evidence/severity; do not restart grace/burst/display. |
| Eligible -> burst start | First opens fixed two seconds; later issues do not extend. |
| Burst ends -> blocked | Retain incident state only; next opportunity revalidates. |
| Eligible -> presented/explained | Mark covered records communicated after successful presentation. |
| Warning -> terminal Error | At most one replacing escalation, subject to spacing except failed stop. |
| Pending -> recovered | Cancel candidate, silent healthy state, no presentation. |
| Communicated -> recovered | Remove obsolete display, start 30-second health latch, silent. |
| Recovered -> fails before re-arm | Reset health interval; preserve communicated suppression. |
| 30 healthy seconds -> armed | Clear resolved suppression/record; next new failure may notify. |
| Scope replaced/disposed | Clear state and cancel owned timers; invalidate old callbacks. |

Parent recovery does not reannounce already communicated child impact. A distinct actionable, previously uncommunicated child is eligible only after current-state/local-explanation evaluation. No unrelated success, online event, dismissal or display timeout is a health transition.

## Settings snapshot

One browser-scoped SettingsService keeps confirmed in-memory values independently of storage. Startup reads valid settings or defaults; default write failure is silent. Explicit change owner captures previous confirmed value, applies effect, commits confirmed session snapshot, then attempts save. Apply failure restores controls/snapshot; partially applied effects require compensation or explicit confirmed mixed/unavailable state. Save failure preserves applied memory state and may create one P2 incident. All FAB/blade readers use the same snapshot, including blocked-storage sessions.
