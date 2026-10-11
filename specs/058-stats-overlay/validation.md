# Stats Overlay implementation validation

## Source and dependency preflight

- The feature depends on the existing `20261010151240_CreateCityRouteHourStatistics` migration and `public.city_route_hour_statistics`; the EF model and `(city_slug, hour_start_utc, route_join_key)` index are present.
- The seven configured canonical cities are `atlanta`, `washington-dc`, `boston`, `new-york-city`, `toronto`, `philadelphia`, and `denver`. Atlanta is the canonical public slug; `marta` is not.
- Configured IANA zones are `America/New_York` for Atlanta, Washington DC, Boston, New York City and Philadelphia; `America/Toronto` for Toronto; and `America/Denver` for Denver.
- The current source configuration has route-hour capture disabled, dry run enabled, a 10-second worker cycle, 30-second default healthy gap limit, and no city gap overrides. Resolved cadence is therefore 30 seconds for all seven cities.
- The checked-in API connection string is blank. No production or disposable database was queried, so actual persistence, retention, coverage and writer/finalizer lag are unknown. Source defaults do not establish retained history.
- The repository does not configure trusted forwarded-client provenance or Stats rate-limit ingress. Azure ingress header behavior, replica count and deployment limits remain operator verification items.
- The optional `before_implement` and `after_implement` extension hooks invoke `speckit.git.commit`; both were skipped under the repository's no-commit instruction.

## Independent aggregate fixture

The fixture in `fixtures/stats-overlay-v1.json` is the source of expected values for contract tests. It is independently calculated from the listed route/hour values, not from a production projection. The fixture establishes the 160 km and 16/20 selected route-hour case; the 30/6 X, 30/2 Y and 180/120 mean case; and the 100 + missing hour + 40 known-prefix case.

## Runtime and release gates

No Stats runtime code, browser prototype, disposable PostgreSQL test, actual retained-history inspection, matched-load measurement, five-visitor review, deployment or exposure change has been completed at this point. These remain open gates. API reads and both client launchers must remain disabled until the feature's G1-G8 acceptance evidence is recorded.
