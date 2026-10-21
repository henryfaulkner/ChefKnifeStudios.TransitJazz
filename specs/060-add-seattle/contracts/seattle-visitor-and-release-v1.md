# Seattle visitor and release contract v1

## Visitor surfaces

- Exactly one picker item and hash route `#seattle`; public display name Seattle, time zone `America/Los_Angeles`, transit-dense Seattle map origin.
- Existing route catalog, map, route selection, reconnect and audio-unlock behavior. Supported KCM and ST vehicles share one soundscape. 512 has no initial map/audio coverage and no invented shape.
- `AudioUnlockOverlay` resolves a Seattle header and three authored paragraphs from `RouteFilterResources.resx`. `InfoFab` resolves companion Seattle text from the same resource system. Both explain live transit becoming music, how to listen, combined KCM/ST coverage, route-512 omission, and required source attribution/terms. Claims describe observed realtime modes; do not promise Sounder or weekday operation from Sunday evidence.
- `StatsCitySeattle` localizes Stats label. Existing route-selected distance scope, category logic, completed-hour windows, `America/Los_Angeles` local date, and All-city sum apply. With absent Seattle reported policy, its reported value is unavailable and All retains Seattle in its coverage denominator with partial qualification under enabled reads. Reported-vehicle count is city-wide only when a valid collection/read policy exists. Pending, partial, unavailable and zero retain their existing meanings.
- Existing category mapping currently treats KCM streetcar as rail and water taxi as bus; acceptance/copy must not misstate these categories.

## Local acceptance

Record a successful solution build and affected existing tests, sanitized source/authentication/mapping/overlap/failure fixtures, identical effective host config, known route geometry, successful local startup, several service-hour updates, plausible map motion and human-confirmed audible output. Confirm local client/backend destinations and exactly one capture owner. Browser verification covers direct link, city switching, filters, reconnection, Stats, desktop/mobile and both themes, plus existing-city regression. An empty history window is pending/unavailable under existing semantics, never fabricated as zero.

## Release gates

No production publication until ST permitted request volume covers actual cycle interval, retries, environments, active instances and overlapping revisions; intended credential access is established through the runtime binding; both direct static service calendars and exception dates cover the intended service; representative service-hour/weekday observations justify the launch claims; effective overrides and capture owner are confirmed. The key-carrying request in the 2026-10-18 research report does not certify credential validation because keyless requests also worked. Reported-vehicle enablement additionally requires a justified cross-source namespace/cadence policy. A normal city addition is expected to need no migration, but release must check pending schema/history gates.

The human release owner reviews the exact diff, commits and controls publication. Record actual backend/client targets, prior healthy versions, intended SHA, secret-reference name (no value), backend-first sequence and recovery owner before any push that can deploy. The separate MartaJazz site is outside scope. If readiness, authentication, publication or existing-city behavior fails, stop exposure and restore recorded healthy server/client versions without destructive data rollback.

## Production acceptance

`validation.md` must identify the running backend/client SHA/revision/workflow, UTC observation times, public `https://transitjazz.com/#seattle`, Seattle readiness/catalog/geometry, multiple processing/publication updates during service, visible motion, audio after unlock, switching and existing-city health. Historical claims require genuine completed-hour evidence. Workflow success alone is not deployed-and-verified status. Pending gates remain explicitly pending.
