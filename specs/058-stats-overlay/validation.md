# Stats Overlay implementation validation

**Navigation simplification (2026-10-12)**: Overlay navigation is withdrawn at the user's request. Stats uses a local visibility flag and the browser's native dialog; Info keeps its existing local open/close behavior. No shared coordinator, history entries/traversals, Info/Stats switching, modal lifecycle manager or special settings replacement is required. Earlier navigation prototype tasks and evidence are historical.

**Scope update (2026-10-12)**: The user has assessed the feature and confirmed it works. Remove the temporary chart prototype and browser-history diagnostic UI. Ship English only: no language selector, saved language preference, runtime language switching or Spanish satellite resource. Earlier Spanish/prototype validation records describe the prior implementation.

## Source and dependency preflight

- The feature depends on the existing `20261010151240_CreateCityRouteHourStatistics` migration and `public.city_route_hour_statistics`; the EF model and `(city_slug, hour_start_utc, route_join_key)` index are present.
- The seven configured canonical cities are `atlanta`, `washington-dc`, `boston`, `new-york-city`, `toronto`, `philadelphia`, and `denver`. Atlanta is the canonical public slug; `marta` is not.
- Configured IANA zones are `America/New_York` for Atlanta, Washington DC, Boston, New York City and Philadelphia; `America/Toronto` for Toronto; and `America/Denver` for Denver.
- The current source configuration has route-hour capture disabled, dry run enabled, a 10-second worker cycle, 30-second default healthy gap limit, and no city gap overrides. Resolved cadence is therefore 30 seconds for all seven cities.
- The checked-in API connection string is blank. No production or operator database was queried. A separate loopback-only, task-owned PostgreSQL 18 cluster was used for disposable acceptance tests; each test created and dropped its own randomly named child database. This establishes SQL behavior on the migration schema, not retained production history, retention, coverage or writer/finalizer lag.
- The repository does not configure trusted forwarded-client provenance or Stats rate-limit ingress. Azure ingress header behavior, replica count and deployment limits remain operator verification items.
- The optional `before_implement` and `after_implement` extension hooks invoke `speckit.git.commit`; both were skipped under the repository's no-commit instruction.

## Independent aggregate fixture

The fixture in `fixtures/stats-overlay-v1.json` is the source of expected values for contract tests. It is independently calculated from the listed route/hour values, not from a production projection. The fixture establishes the 160 km and 16/20 selected route-hour case; the 30/6 X, 30/2 Y and 180/120 mean case; and the 100 + missing hour + 40 known-prefix case.

## Completed local checks

- Shared contract tests: 5 passed, 0 failed, covering canonical identity, finite vocabularies, explicit nullable JSON fields, and rejection of unknown fields.
- WebAPI policy and independent acceptance fixture tests: 10 passed, 0 failed. These cover UTC grace cutoffs, local-day/DST boundaries, the Kathmandu fractional-hour case, and frozen aggregate examples.
- PostgreSQL 18 query tests: 7 passed, 0 failed using the explicit disposable database only. They execute the parameterized one-statement query against migrated tables and cover whole-cohort filtering, unavailable/zero states, both windows, city and All outputs, ordinal keys, metadata/category changes, category and route capacity limits, and concurrent quarantine visibility. A PostgreSQL parse failure found during the first run was fixed before the passing rerun.
- `Server.Data` build: succeeded with 0 warnings and 0 errors. `wasm-tools` workload installation completed successfully.
- WebAPI test builds report the existing `Microsoft.OpenApi` 2.0.0 advisory (`GHSA-v5pm-xwqc-g5wc`) and pre-existing nullable/analyzer warnings.

## Runtime and release gates

The fixed-data browser prototype completed the G2 fixed-model matrix; the actual root-map G3 ownership evidence is recorded below. Actual retained-history inspection, finalizer/retention-lag measurement, supported-bound/memory profiling, matched load, five-visitor review, deployment and exposure change are still pending. The disposable PostgreSQL tests do not satisfy the real retention, lag, load or operator gates. API reads and both public client launchers remain disabled until the feature's G1-G8 acceptance evidence is recorded.

### Browser G2 partial evidence (2026-10-11)

- The fixed page at `/stats-prototype` renders seven canvas charts with stable canvas identities through data/theme/language changes. The timeline fixtures show a 23-hour spring day (01:00 through next local midnight, omitting 02:00) and 25-hour fall day (both 01:00 offsets, through next midnight). City numeric ticks and evidence use interval end time. Coincident zero-origin bubbles remain individually visible; the minimum-radius mean is presented as `0.001`, not rounded to zero.
- A four-second delayed Chart.js response began with zero canvases; replacing sample data and switching dark mode before resolution produced all seven charts with the newest 55 km summary, 33/22 category values and dark theme. There were no chart errors. A 6.5-second delay exceeded the bounded per-chart initialization deadline: three charts initialized while four displayed localized failure alerts, with no renderer exceptions. This is accepted timeout/fallback evidence, not a requirement that the entire seven-chart page initialize inside the artificial delay.
- With the Chart.js URL returning 404, the app booted normally, kept seven figure/table summaries and localized error alerts, rendered zero canvases, and left the keyboard-expandable pie table usable with 30/20/0/unavailable category evidence and 60/40/0 shares. Replacing data/theme while blocked retained the updated 55 km summary and 33/22 rows with all seven figures in fallback. A usable table remains available through failure.
- Saved Spanish startup and live English↔Spanish switching update document `lang`, chart headings/tables/coverage, visibility controls, launcher text and number formatting without reload. The regression was traced to a first English fallback lookup before satellite loading; `App` now waits for language initialization before rendering `Router`. StatsFab and Info labels use selected-language lookup plus event repaint.
- Browser checks also confirmed dark chart frames/light text, stable seven canvas IDs, keyboard and native legend visibility synchronization, hidden-city persistence through reorder, hide-all/Show all, unmount/remount reset, chart-kind remount, rapid close/reopen, and 320px table containment with keyboard-focusable horizontal scrolling. The expanded All table does not widen the document.
- T019 is complete. The focused fixed-model browser matrix passed for 23/25-hour DST axes, repeated offsets, raw contribution/table equality, seven canvases, current model/theme before delayed initialization, language reprojection, native and keyboard visibility/reorder, hide-all/Show all, kind remount, rapid close/reopen, disposal, blocked/late Chart.js fallback tables, 320px containment and dark axis contrast. Full screen-reader/touch/200%-zoom acceptance and maximum-route/capacity evidence belong to T069 and T080-T083.

## Implementation progress — 2026-10-11

This earlier implementation checkpoint is superseded by the latest reconciliation below (66 of 88 tasks complete; 22 pending). Local work completed to date includes shared wire contracts, UTC/window policy, the bounded SQL snapshot query and projection, cache ownership/admission, API endpoint/rate limiting, opt-in client transport, finite chart display models and the fixed chart/browser proof. Server and client feature exposure defaults remain disabled.

- Focused Shared contract tests: 5 passed, 0 failed.
- WebAPI policy and independent fixture tests: 10 passed, 0 failed.
- PostgreSQL SQL acceptance tests: 7 passed, 0 failed on the isolated PostgreSQL 18 disposable instance.
- WebAPI cache and endpoint tests: 11 passed, 0 failed, including actual 429 content type, Retry-After and spoofed forwarded-header behavior.
- Client language, resource parity and transport tests: 8 passed, 0 failed.
- Client.WebApp build: succeeded with 0 errors; existing warnings remain.
- Cache size admission currently uses a documented conservative object-size estimate; heap measurement remains pending T080.

G2 fixed-model checks and the G3 owner/history browser matrix have passed. Remaining browser accessibility, All/refresh acceptance, actual retained-history and lag checks, full memory/load profiling, visitor review, final publish and operator rollout/withdrawal remain pending. No API or public launcher exposure has been enabled.

- Pure chart model and transport regression tests: 14 passed, 0 failed, covering finite/aligned series values, elapsed X ordering, raw stacks and gaps, keyed theme styles, pie row retention, bubble omissions/radii, safe null-success responses and existing transport metadata behavior.

### Browser G3 and modal layout follow-up (2026-10-11)

- Parent browser pass confirmed Stats to Info to Stats uses one native modal, keeps the same URL and seven underlying canvas IDs, returns focus to Open Stats after Escape, supports Close autofocus and keeps Tab in the dialog. The CDP Alt+Left/Right keypress did not invoke browser traversal and is not counted as a product failure.
- Actual Browser Back/Forward controls moved the framework index 1→0→1 and restored the same owned entry/session/surface; reload removed a stale marker without traversal. At 320px the dialog is the sole scroll owner; wheel scrolling and sticky safe-area Close were verified, and closing restored the document's previous scroll styles/position.
- Added a visible DEBUG-only unrelated top-level history-state fixture. An initial browser regression showed native dialogs while ownership remained empty after adding that field. The JS helper now treats framework empty userState and browser null/absent userState as equivalent only for source verification and exposes its capture/merge result in the DEBUG panel. The fresh-origin retest passed: adding the fixture, Stats open, Stats→Info replacement, Back, Forward, Info→Stats replacement, explicit close, Forward and reload preserved the unrelated top-level field; push/replace kept the framework index and entry/session identity, Back/close removed ownership as expected, and reload cleared the stale marker without traversal. No modal errors occurred. The later root-map/browser pass below closes the welcome/map/history/foreign-state items that were pending at this checkpoint.
- The same browser run exposed a possible late `showModal()` after capture rollback. Stats and Info now use a serialized, generation-aware dialog lifecycle that closes an open completed after ownership changed and waits for in-flight dialog work during disposal. Two delayed-show rollback/disposal tests plus the three navigation-service tests pass (5/5).
- Debug WebApp build succeeded with 0 errors (6 existing nullable warnings). Focused Client.Shared Stats/navigation/Pax tests previously passed 42/42 (0 failed); latest focused lifecycle/navigation tests passed 5/5.
- Browser verification of the complete welcome/map/audio/settings/foreign-navigation/reduced-motion matrix remains pending; T023 remains unchecked.

### G3 root-map/history owner follow-up (2026-10-11)

- On the real `/#atlanta` map path, silent welcome Stats entry opens one native dialog while audio remains locked/muted. Stats/Info substitution, Back/Forward, explicit close, foreign history adoption, unrelated structured-clone fields (including BigInt and cycles), reload cleanup and reopen all preserved framework history ownership. Back to a prior Atlanta Stats marker after navigating to Denver was correctly ignored as an obsolete city session.
- Within-page Stats/Info operations preserved the existing map ID, SignalR object and synth module identity. History operations did not unlock audio. Full reload naturally creates new instances. The actual audio unlock gesture and live playback/SignalR data still require a configured feed/runtime.
- Reduced-motion browser emulation was unavailable in the browser control surface; the CSS media query is present, but that runtime result remains pending. The Settings-to-Stats keyboard path passed; a distinct pointer path while the Settings drawer covers the launcher was not established.
- API-backed route selection and session work now has a shared visibility event (no second document listener), a latest-generation single-flight request gate, ordinal response-selection validation and a tested 60-second-first retry policy. Scope and route selection changes preserve cooldown; the newest request starts after the canceled request settles.
- At this earlier checkpoint, focused Client.Shared tests reported 107 passed, 0 failed. The latest full Client.Shared and WebApp results are recorded below. T033, T035 and T037 are checked; the integrated fixture-host browser pass for healthy/zero/pending/partial/unavailable DTOs and selection transitions is still pending.
- Projection tests now preserve complete/partial/pending/unavailable category evidence, explicit null share amounts, server-provided shares and route ratios, unknown-category fallback, stationary/omitted bubbles, raw per-category stacks, and known amounts through resumed, final and all-point plot gaps. Timeline tooltip lines localize coverage and metric labels instead of exposing wire codes.

## Latest implementation and evidence reconciliation (2026-10-11)

The prior implementation checkpoint above reported **70/88 complete; 18 pending**. The current reconciled count is **74/88 complete; 14 pending**: T023, T043, T050, T057, T069, T076, T078-T081, T083-T084, T087-T088. These cover remaining reduced-motion/accessibility and browser acceptance, retained-history/lag, capacity/browser and matched-load gates, visitor review, and operator deployment/withdrawal. API and public launcher defaults remain false.

### Latest code checks

- Client.Shared test project: **139 passed, 0 failed, 0 skipped** after adding controlled request/session, atomic snapshot, cooldown, and refresh-lease lifecycle tests.
- Focused Client.Shared Stats/session/retry/resources/transport/feature-flag suites: **34 passed, 0 failed, 0 skipped**.
- Current-source WebApp Debug build: **succeeded, 0 errors, 6 nullable warnings**.
- Session cases use supplied instants to cover the one-minute refresh boundary, after-midnight local-date change before the grace cutoff advances, the next completed-hour cutoff, generation/normalized selection rejection, atomic snapshot/projection replacement, language-only reprojection, and actual stale timestamp retention. Retry cases verify first 60-second delay, bounded backoff, Retry-After, success reset, and preservation of the original failure cause across scope/retry/visible attempts.
- Refresh-loop lifecycle coverage observes cancellation of the lease token for a pending delay, starts a new lease on visible restoration, and proves a late completion cannot clear the new owner. Closed/hidden/disposed loop eligibility is tested as a policy. This is source/project test evidence, not a browser measurement of timer callback counts while hidden.
- WebAPI `StatsOverlay` focused suite: **44 passed, 0 failed, 0 skipped** with `ROUTE_HOUR_HISTORY_DISPOSABLE_CONNECTION` set only for that test process to the owner-provisioned loopback PostgreSQL 18 fixture.
- Parent reported existing Worker regressions **186/186 passed**. Earlier Shared contract, WebAPI policy/fixture, query and focused navigator/audio tests are listed in the preceding checkpoint sections. This was an earlier partial checkpoint; final coordinated regression results are below.
- No operator database was used, and no migration/index, production configuration, deployment or public flag was changed. No commit was created.

### Browser follow-up after the latest render fix

The current Debug integration browser check initialized all three visible charts after fixing a `DatasetVisibility` component-parameter type mismatch in `SeriesChart`; complete timeline rows now show a neutral “None” reason instead of implying unavailable evidence. The same pass confirmed saved-Spanish startup and live ES→EN→ES reprojection for the header/cards/document language, stable canvas identities, and a lazy table disclosure whose pointer, Enter and Space states agree with the native disclosure and its rows. During a 1.5-second delayed response, all seven historical route buttons and the selected key remained mounted. Desktop layout used two 416px chart columns without page overflow. The parent is still checking narrow/All/error/disabled-flag cases on this build.

The root-map G3 pass verified silent Stats access from the welcome/map surface with audio locked and muted; Stats/Info replacement, Back/Forward, explicit close, foreign state adoption, reload cleanup/reopen, structured-clone unrelated fields (including BigInt/cycles), and obsolete Atlanta entry rejection after changing to Denver. The map, SignalR object and synth module identities stayed stable during within-page modal operations. A native welcome gesture unlocked the existing synth, and the Stats mute action stayed synchronized with AudioFab. Live playback/feed behavior and reduced-motion emulation were not available in the browser harness; do not claim those results.

### Synthetic capacity and query-plan evidence

A disposable PostgreSQL 18 fixture with 98,304 rows (4,096 routes × 32 categories × 24 hours) was measured using the current capped response writer and indexed C# lookup implementation. Results are single synthetic observations, not production p95:

| Scenario | Result | Elapsed | Raw / gzip response |
| --- | --- | ---: | ---: |
| 4,096 ordinary keys, unfiltered | Complete; 4,096 routes and bubbles, 32 categories; 94,208 today km, 98,304 last-24 km | 1,974 ms | 4,779,040 / 96,544 bytes |
| 4,096 ordinary selected keys | Complete; same route/category bounds and measures | 1,647 ms | 6,519,827 / 156,251 bytes |
| 4,096 keys of 512 bytes, unfiltered | Complete response refused with 422 `response_too_large` | 3,116 ms | No partial success |
| 4,096 selected 512-byte keys | 422 `response_too_large`; request was 2,109,495 bytes | 3,125 ms | No partial success |
| 4,097 route union | 422 `scope_too_large` | 1,168 ms | No partial success |

The bounded response writer avoids producing a partial 8 MiB-exceeding response. Sampled managed peak for the oversized selected run was 352,054,536 bytes, but this process-level sample includes pools and prior reads; it is **not** isolated cache-object or four-read peak memory evidence. The conservative cache-admission estimate is not a heap measurement.

The latest parameterized SQL EXPLAIN uses the same 98,304-row disposable fixture. Planning/execution (ms): ordinary unfiltered 3.521/1,660.567; ordinary selected 3.487/1,694.573; 512-byte unfiltered 3.848/3,205.170; 512-byte selected 10.783/3,354.401. The planner selected sequential scans for this synthetic fixture; no existing index usage is claimed. All four query times exceed the 250ms prepared-query target, so matched production-like query performance remains an open G7/G8 gate. No index migration or global PostgreSQL tuning is justified by the available evidence. Durable raw plans and query text are under [`fixtures/evidence`](fixtures/evidence/).

### Final regression, release artifact, and browser reconciliation

- Full regression set before the latest client-only test additions passed with zero failures and zero skips: Shared **38/38**, Client.Shared **129/129**, WebAPI **291/291**, TransitDataWorker **186/186**. Current Client.Shared then passed **139/139**. The WebAPI run used the isolated PostgreSQL admin DSN only through `ROUTE_HOUR_HISTORY_DISPOSABLE_CONNECTION` and `CITY_CATEGORY_INSIGHTS_DISPOSABLE_CONNECTION`; both fixture families create/drop random child databases. The earlier 52 skips were CityCategoryInsights PostgreSQL facts/theories whose attribute explicitly says to set `CITY_CATEGORY_INSIGHTS_DISPOSABLE_CONNECTION`. They were not operator-host or API-key cases. With the second variable set to the same owned disposable cluster, all passed and there were no skips. A timezone-sensitive test normalizes its offset result using `.GetDateTime().ToUniversalTime()`; one migration test expects the additive city/category table alongside the existing route-hour table.
- Current-source local Release API publish and trimmed WASM publish succeeded. The clean output and SHA-256 manifest are recorded in [`release-handoff.md`](release-handoff.md); API `StatsOverlay.Enabled` and client `AppSettings.FeatureFlags.StatsOverlay` are both false. The Spanish satellite, ICU data, local Chart.js/license and callback module are included. Existing client workflows install `wasm-tools`, publish Release WASM with compression disabled, and upload `publish/wwwroot`. The application retains its existing MapLibre CDN dependency.
- On the current-source trimmed Release artifact, saved-Spanish startup rendered seven historical route options, 50/160 city cards, and three nonzero canvases. The public English control changed document language and the chart header while preserving all three canvas identities. This closes T068's current-artifact culture/runtime check; public flags in the artifact remained false.
- Latest integration-browser pass accepted the current chart render fix, lazy tables, preserved route selector during loading, desktop 2-column and narrow 320px layout, All city dates/zones, localized visibility controls, and false-flag launcher absence. The maximum-route fixture's browser driver timed out on frame-tree inspection; Escape recovered the dialog, the map remained usable, and the fixture continued polling. This does not establish a product freeze or accepted maximum-render latency; T081 remains open.
- A delayed healthy request was closed by Escape and followed by a new session/zero summary; when the fixture eventually completed its older request, it did not repopulate the closed/reopened presentation. The fixture intentionally ignores cancellation, so its observed server-side active count of two is not evidence about the API's one-client-request gate. Cooldown browser evidence also preserved the original localized `read_timeout` reason across an All scope change, left Retry disabled for its 90-second `Retry-After`, made no extra request during cooldown, and exposed no raw-detail sentinel or false empty route list.
- T041's layout/runtime portion is complete: 320px document width stayed within the viewport; modal content was the only scroll owner, sticky close stayed visible, and narrow reading order was pie, totals, bubbles, timeline. Remaining integrated screen-reader/touch/200%-zoom and reduced-motion runtime checks remain T069/T023 gates.
- T085, T086, and T068 are complete for the local handoff/current-source artifacts and culture runtime evidence. G6/G7/G8 operator gates are not claimed complete. No deployment, public flag change, operator database access or commit occurred.

## Production cleanup (2026-10-12)

The user assessed the current feature and confirmed it works, then requested removal of prototype elements and Spanish/language options.

- Removed `/stats-prototype`, the browser-history diagnostic component and styles, all diagnostic panel mounts, prototype-only history fixtures/helpers, and the synth/SignalR diagnostic APIs used by those panels. Production modal/history coordination remains covered by the existing regression tests.
- Removed `RouteFilterResources.es.resx`, Settings/Stats language controls, the persisted language field, language service/event/subscriptions and the all-cultures WASM setting. Startup sets English culture explicitly and the static document declares `lang="en"`; saved language fields are ignored while the existing Settings version/audio/theme schema remains compatible. Neutral English resources remain the copy source.
- Updated the existing resource, chart-text, projection and session tests and the current feature requirements/tasks for English-only behavior.
- Client.Shared xUnit: **134 passed, 0 failed, 0 skipped** (`dotnet test ...Client.Shared.Tests.csproj --no-restore --verbosity quiet -p:WarningLevel=0`).
- Release WASM publish succeeded with `BlazorEnableCompression=false`. Output: `C:/Users/hfaul/AppData/Local/Temp/transitjazz-stats-cleanup-20261012/client/wwwroot`. Inspection found no Spanish satellite files or prototype/debug/language-control styles, confirmed `lang="en"`, and confirmed local Chart.js 4.5.1 and the chart callback module remain present. Existing compiler/analyzer warnings remain.
- Both edited JavaScript modules (`overlay-navigation.js` and `transit-synth.js`) passed Node syntax checks. No additional browser, API/database, load or deployment checks were run for this client cleanup; earlier evidence above is historical.

## Local overlay state simplification (2026-10-12)

At the user's request, removed OverlayNavigationService, OverlayNavigationCoordinator, ModalDialogLifecycle, the overlay-navigation.js module, their DI registration/tests, Stats/Info switching controls/resources and the forced settings-close extension. Info retains its existing local open flag. StatsFab owns one local open flag; the welcome button reuses the existing app event channel, and StatsOverlay receives IsOpen/OnClose. A five-line stats-dialog.js helper calls native showModal/close for focus containment, inert background and focus restoration. The Stats request/cancellation/cooldown behavior remains covered by its existing tests.

- Client.Shared xUnit: **117 passed, 0 failed, 0 skipped**. The 17 removed tests exclusively covered the withdrawn navigation/lifecycle classes.
- Native dialog helper: **1 passed** (`node --test src/Client/ChefKnifeStudios.TransitJazz.Client.Shared.Tests/StatsDialogTests.mjs`), covering idempotent open/close and detached-dialog cleanup.
- Chrome local browser smoke passed for welcome and map Stats launchers, native modal focus/Close autofocus, Escape, close/reopen, focus restoration, unchanged URLs, independent Info open/body-close and reload returning to welcome with Stats closed. Ordinary Back navigated away from a local layout test route while Stats was open, without consuming a synthetic overlay history entry. Audio was not explicitly unlocked by the checks.
- The local API was unavailable; the browser exercised the safe transport-error presentation. This smoke does not replace prior chart/data/load acceptance evidence.
- Release WASM publish succeeded with BlazorEnableCompression=false at `C:/Users/hfaul/AppData/Local/Temp/transitjazz-stats-local-state-20261012/client/wwwroot`. Published client assemblies contain no removed navigation/lifecycle types; the old overlay-navigation.js asset is absent and stats-dialog.js is present. Existing compiler/analyzer warnings remain.
- Updated current spec/plan/UI/data contracts to remove shared history and overlay navigation requirements; earlier navigation evidence/tasks are marked historical. Changes remain uncommitted.

## Audio preference rollback (2026-10-12)

At the user's request, removed AudioPreferenceChannel and its three StatsAudioTests, restored AudioFab.razor to its original implementation, and removed the Stats mute/unmute control, preference subscriptions, unlock-state checks, synth injection and unused audio labels. The original AudioFAB/settings audio controls remain responsible for their existing settings/event behavior. Updated current requirements and tasks so Stats has no audio preference behavior.

- Release Client.Shared tests: **114 passed, 0 failed, 0 skipped**.
- Release client build succeeded through the local development host.
- Chrome check passed: the original AudioFAB toggles volume_up/volume_off; Stats exposes no audio control; opening and closing Stats preserves the muted state; the test restored the original audio setting afterward. Audio was not explicitly unlocked. The local API was unavailable, so Stats showed its safe loading-error presentation.
- Source search found no remaining AudioPreferenceChannel, Stats audio-control or preference-subscription references. AudioFab.razor has no diff against its original tracked implementation. Changes remain uncommitted.

## Shared MatBlazor route filter styling (2026-10-12)

At the user's request, Stats and map RouteFilters now use one shared RouteFilterList presentation and stylesheet: compact colored MatButton pills, MatSubtitle1 category labels with keyboard-operable MatButton selection, MatIconButton category clearing, and MatButton bulk actions. Removed the separate Stats button/card style and the map-only presentation switches. Stats uses stable category chart colors and the same unselected-route muting; availability/category-change notes remain visible with full labels and are included in accessible names. Each component instance has its own category heading IDs. The adapters retain independent selection state.

- Release Client.Shared tests: **115 passed, 0 failed, 0 skipped**, including historical notes, ordinal selection and unselected-route emphasis in the Stats projection.
- Release WebApp build succeeded through the local development host.
- Chrome with loopback-only synthetic Stats/GTFS responses confirmed 60px MatBlazor route buttons, map-style rounded colored pills, keyboard Space/Enter toggles, category select/clear and whole-list select/clear actions, distinct category heading IDs and preserved map selection after Stats changes/close.
- Light/dark theme checks passed, including dark muted-route colors. The 320px viewport had 320px document width and no horizontal overflow; desktop 1280px layout also fit. Historical availability/category-change notes remained visible. Test audio/theme settings and viewport were restored afterward.
- Updated the plan/UI contract to describe the shared MatBlazor presentation. Synthetic browser checks do not claim production API/history/load acceptance. Changes remain uncommitted.

Follow-up: Stats now wraps RouteFilterList in the same MatAccordion, bound MatExpansionPanel, summary/header and details structure used on the map. The existing RoutesLabel labels the collapsed header; the duplicate inner Routes heading is removed. Expansion is local and initially collapsed. Release WebApp build and all **115 Client.Shared tests** passed. Chrome with the same synthetic fixture confirmed expand/collapse, preserved route selection on reopening, shared filter styling in both themes, no panel in All mode and a settled 320px layout without horizontal overflow. Browser theme/viewport settings were restored; changes remain uncommitted.

Route color follow-up: Stats now reads the existing application route-shape cache to match the map's per-route GTFS colors, using the same RouteColorFallback for missing/washed-out colors. Unmatched historical routes keep category colors; city identity and case-sensitive route keys prevent unrelated matches. Stats updates its presentation when the existing cache finishes loading, without fetching or awaiting live map data or changing map/audio selection. Release WebApp build and all **116 Client.Shared tests** passed. The new regression covers distinct A/a colors, shared white/black/missing-color fallbacks, historical/other-city options and untouched map selection. Chrome synthetic fixtures confirmed matching yellow/green/teal/red/blue swatches in Stats and map, preserved category fallback for a removed route and independent selected-route emphasis. Changes remain uncommitted.

Bubble color follow-up: Route bubbles and Stats pills now share ChartPresentation.GetRouteColor, including cached GTFS colors, city/ordinal route identity, white/black fallbacks and historical category colors. Bubble rows/points carry an optional explicit color through size ordering; the Chart.js dataset uses it consistently in both themes. City bubbles retain their keyed theme palette. The existing route-metadata event replaces the accepted presentation locally so chart fills and pills update together.

- Release Client.Shared tests: **117 passed, 0 failed, 0 skipped**. Added pill/bubble agreement after size sorting and metadata arrival, preserved coordinates/radii, case-sensitive colors and historical/other-city fallback checks; All-city bubbles keep their city color keys.
- Release WebApp build succeeded through the local development host.
- Chrome synthetic fixture: yellow bus, green rail and red stationary-route bubbles matched their filter pills in light and dark mode. With GTFS metadata delayed 15 seconds, Stats first showed category fallbacks, then both pills and plotted bubbles updated to route colors from the same snapshot. Fixture Stats request count stayed at seven across that update. Browser theme was restored; no audio unlock or preference change was made.
- Updated the current plan. Changes remain uncommitted.

## Shared transport-category colors and chart dark mode (2026-10-12)

TransportCategoryColors now supplies running-label dots, category pie slices and cumulative category lines from one palette: bus green, rail blue, streetcar/tram red and neutral fallback, with the label's existing lighter dark-mode shades. Category-prefixed series keys resolve directly to transport colors, fixing the matching teal Bus/Rail lines caused by hash collisions. GTFS route bubble/pill colors remain shared; unmatched route colors follow the current transport theme. Theme changes rebuild accepted presentation locally. ChartFrame now receives IsDark from every typed chart and applies the same text/surface/grid colors as the canvas, fixing dark text/axis styling on a light card.

- Release Client.Shared tests: **125 passed, 0 failed, 0 skipped**. Coverage includes canonical/prefixed category equivalence, label palette values and aliases, neutral fallbacks, dark colors and route bubble/pill agreement in both themes.
- Release WebApp build succeeded through the local development host.
- Chrome synthetic fixture confirmed green Bus and blue Rail pie slices/lines/legend in light mode, lighter green/blue shades in dark mode, distinct cumulative lines, and themed chart cards across pie/bubble/line. Computed card styles changed from light surface rgb(245,245,245)/text rgb(26,28,30) to dark surface rgb(26,28,30)/text rgb(226,226,230). Axes, captions and table controls were visually readable. Existing per-route bubble colors remained visible.
- Updated the current plan; browser theme was restored. No audio unlock, deployment or commit occurred.
