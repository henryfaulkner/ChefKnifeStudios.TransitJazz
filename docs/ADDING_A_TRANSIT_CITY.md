# Adding a city to the TransitJazz catalog

Follow this guide from a feed viability spike through a verified production release. Work through one numbered action at a time; finish each section's verification before continuing. If a check fails, resolve it or record a blocked decision rather than assuming success.

This is a reusable procedure, not evidence that a particular city has been deployed. Replace `<City>`, `<Agency>`, `<city-slug>`, and `<feature-folder>` in the prompts. Run commands from the repository root in PowerShell. The city slug is the full place name in lowercase with hyphens, such as `washington-dc`; the agency name can remain the C# constant identifier.

Repository details were checked on **2026-10-18**. Re-read current code and workflow files when executing the guide: older onboarding skills and compatibility reports do not cover every current registration requirement.

## 1. Spike the feed's viability

**Outcome:** A measured compatibility report and an explicit go, conditional-go, or no-go decision before implementation begins.

1. Ask the AI to evaluate the agency using the data exploration and feed decoding skills. Start with official agency documentation, then fetch actual feeds. Existing reports are research leads, not proof of current health.

   ```text
   $mj-data-explorer Evaluate <Agency> in <City> for TransitJazz inclusion.
   Use mj-gtfs to inspect current static GTFS, GTFS-RT vehicle positions,
   and any separate rail API. Verify endpoints, terms, authentication and
   rate limits against official sources. Read the current worker route
   matching code. Save dated findings to docs/city-compat/<agency-slug>.md.
   Do not implement or deploy. Mark anything not measured as unverified.
   ```

2. Have the report cover the following evidence. Sample during service hours and across multiple feed updates; an HTTP 200 or one empty snapshot is insufficient.

   | Inspect | Evidence to retain |
   | --- | --- |
   | Access and permitted use | Official URLs, access method, licensing/attribution requirements, quotas, documentation date |
   | Static GTFS | Decodable ZIP; routes, trips, shapes and the stops/calendar data needed by the proposed adapter; current service coverage |
   | Live positions | Decodable payload, timestamps/freshness, coordinates, stable identifiers and route references; successful-empty versus failed requests |
   | Route matching | Sample joins from realtime IDs to static `route_id` and `route_short_name`, unmatched counts and denominators, geometry coverage, collisions |
   | Modes | Which buses, streetcars, subway, commuter rail or other modes are actually present; which are absent or inferred |
   | Operational fit | Response size, observed update frequency, request budget, expected worker load and any required polling changes |

3. Choose the implementation path from that evidence. Prefer the generic `GtfsRtCity` when its format, authentication, route matching and polling behavior fit. Use a focused authentication or mapping change when that is the only gap. Plan a bespoke adapter for incompatible payloads or separate rail data. Position synthesis is a separate design decision requiring suitable source data and truthful visitor copy.

4. Record the decision and remaining blockers. Calculate request demand across endpoints, active instances, environments and retries: a continuously polled endpoint requires roughly `86,400 / polling interval in seconds` requests per day per instance, before retries. Compare this with the agency's actual quota. Do not assume a key solves format or quota problems.

**Verification — pass before planning:** The report contains dated measurements, sample route joins, explicit mode coverage and the chosen implementation path. A no-go stops onboarding. A conditional-go may proceed to key acquisition, but authenticated feed compatibility must be verified before implementation. If service was inactive, resample rather than treating absence as compatibility.

## 2. Key acquisition and credential setup

**Outcome:** The feed works with the application's actual authentication mechanism, without credentials entering source control or prompts.

1. Consult [Key acquisition](city-compat/KEY_ACQUISITION.md) for existing research, then recheck the agency's official instructions. A human completes account registration, terms acceptance and key issuance. Record the owner, renewal process, scopes and quota in the compatibility report without recording the key.

2. For a keyless feed, record that result and skip credential provisioning. For a protected feed, distinguish query-parameter authentication, headers, OAuth and static-download authentication. Inspect both `GtfsRtCity` and `GtfsStaticLoader`; do not assume the live-feed key configuration authenticates static downloads too.

3. Make the local credential available through the exact environment variable named by `ApiKeyEnvVar`. Enter the value through a trusted local secret mechanism or a concealed prompt; do not put it in tracked `appsettings` files, launch profiles, command history or chat. The current generic adapter reads this value using `Environment.GetEnvironmentVariable`, so a .NET user-secret value alone does not satisfy it.

4. Validate the actual request construction. The generic configuration exposes `ApiKeyEnvVar` and `ApiKeyQueryParam`; these are not a general header-authentication facility. Check existing query strings and URL encoding. Put any missing auth capability in the implementation plan, with tests that never log the secret.

   ```text
   $stepwise-instructions Help me configure <Agency> credentials locally.
   Inspect the current adapter and static loader first. Give one action at
   a time, use concealed entry, and ask only for redacted status/results.
   Verify a real authenticated decode without printing the credential or
   an authenticated URL. Record credential names, never their values.
   ```

**Verification — pass before implementation:** Live and static requests both succeed and decode using the intended authentication path, or the precise code gap is documented and covered by the plan. The account quota supports the proposed workload. No credential values appear in the diff or evidence files. Repeat Section 1's measurements after gaining access to previously gated feeds.

## 3. Scope, specification and implementation plan

**Outcome:** A reviewable feature with tasks covering the whole release, including verification.

1. Use the onboarding skill as an orchestration aid. Give it the compatibility report and this guide so it includes current requirements beyond its older four-registration checklist.

   ```text
   $add-transit-city Prepare onboarding for <Agency>, <City>, slug
   <city-slug>, using docs/city-compat/<agency-slug>.md and
   docs/ADDING_A_TRANSIT_CITY.md. The selected path is <generic / focused
   extension / bespoke adapter>. First prepare the specification and plan;
   include all registration, statistics, visitor-copy and release tasks.
   Do not commit, push, migrate or deploy. Never run auto-commit hooks.
   ```

2. Run the skills in sequence: `speckit-specify`, `speckit-clarify` if needed, `speckit-plan`, then `speckit-tasks`. Run `speckit-analyze` to find contradictions or omissions before implementation. Invoke each separately and review its output before the next.

3. In the feature artifacts, record the canonical slug, display name, IANA time zone, feeds, credential variable names, included modes, route-identity policy, adapter choice, source identity policy, polling budget and known limitations. Specify local acceptance criteria and production rollback criteria.

4. Create `specs/<feature-folder>/validation.md` as the running evidence record. Use columns for **check, environment, UTC time, result, evidence link, unresolved issue**. Keep measurements and deployment results separate from planned checks. All commits remain the user's responsibility under [AGENTS.md](../AGENTS.md).

**Verification — pass before editing runtime code:** The compatibility report is linked, the design resolves the adapter/authentication decision, and tasks cover Sections 4–10. There are no unresolved blockers masquerading as implementation assumptions. The feature has a place to record actual results.

## 4. Config changes and city registration

**Outcome:** The application recognizes one consistent city identity across feeds, history, API and client.

1. Run `speckit-implement` against the reviewed tasks, asking it to stop at the local verification boundary before release actions. Use the following inventory to review its work; file paths are relative to `src/`.

   | Registration | Location and required change |
   | --- | --- |
   | Canonical identity | `ChefKnifeStudios.TransitJazz.Shared/CityNames.cs`: add a constant whose value is the full city slug |
   | Live-feed configuration | `Server/ChefKnifeStudios.TransitJazz.Server.TransitDataWorker/appsettings.json`: add the `Cities` entry |
   | Production host/static loading | `Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/appsettings.json`: add the same city configuration |
   | Environment overrides | Inspect both hosts' `appsettings.Development.json`, other environment files and deployed environment overrides for indexed `Cities` entries |
   | History selection | Review explicit `HistoricalStatistics.Cities` lists and cadence overrides; include the city where collection is intended |
   | Stats display identity | `Server/ChefKnifeStudios.TransitJazz.Server.WebAPI/Statistics/StatsOverlayOptions.cs`: extend `DisplayNameKeys`; add the corresponding `StatsCity…` resource |
   | Visitor selection | `Client/ChefKnifeStudios.TransitJazz.Client.Shared/Components/FABs/CityFab.razor`: add the city button and hash/reload handler |

2. Keep the two base `Cities` entries identical. Include `Name`, `TimeZoneId`, `GtfsRtUrls` and `StaticZipUrls`, plus only justified authentication, mapping or additional-source settings. Encode literal spaces in URLs. Preserve existing array order: current host code includes an index-based rail configuration reference, and environment configuration can override array indices.

3. Review the **effective** configuration, not just base JSON. .NET configuration combines indexed array settings; an environment-specific list can replace fields at matching indices while leaving other base values. Verify each resulting city name, endpoint count, time zone and adapter mapping without dumping secrets.

4. Configure reported-vehicle sources deliberately. Inspect `Cities/CityConfig.cs` and `Statistics/ReportedVehicleCatalogBuilder.cs` in the worker. Where supported, provide one policy per endpoint in matching order, with stable `SourceKey`, `IdentityNamespace`, identity rule and measured cadence/gap limits. Endpoints sharing a vehicle ID namespace must not double-count the same fleet. Do not invent a fallback identity when descriptor IDs are absent.

5. Review enabled statistics features and their current release contracts. Record unsupported reported-vehicle coverage explicitly. Do not enable global collection/read flags merely to add a city; if already enabled, validate the new city against their startup and persistence requirements. Check configured city/source/route limits and existing observability city filters.

**Verification — pass before visitor content:** Build with `dotnet build src\ChefKnifeStudios.TransitJazz.sln`. Require zero errors and investigate newly introduced warnings. Confirm effective city entries agree, time zones resolve, the Stats display key exists, source-policy validation passes, and historical selection is intentional. A successful build alone does not prove startup configuration is valid.

## 5. Worker adapter creation or reuse

**Outcome:** Raw agency data becomes correctly identified, correctly located TransitJazz vehicles.

1. For a generic-compatible feed, reuse `GtfsRtCity` and record “no new adapter required.” Verify any `RailRouteIdMap` or `RouteIdNormalization` against measured joins; do not add normalization speculatively.

2. For a bespoke path, implement the reviewed `ITransitCity` integration and register it in **both** the standalone worker and WebAPI `Program.cs`. The production WebAPI hosts the worker. Review `MartaCity` and `NymtaCity` as examples, while keeping the new behavior specific to the agency's actual contract.

3. Preserve cancellation, bounded requests, partial-source failure handling, safe structured logging, route identity, mode classification and static geometry matching. If reported-vehicle capture applies, capture raw source observations before route mapping or synthesized positions and preserve them through result combination.

4. Use the testing skill for meaningful adapter tests: representative decoded data, missing fields, stale timestamps, failed/empty sources, authentication construction, route collisions and multiple-source identity handling. Use sanitized fixtures; keep live probes as separate integration evidence.

   ```text
   $util-testing Review the <Agency> adapter against its compatibility report
   and plan. Add focused tests for source decoding, route joins, auth, failure
   behavior and identity handling where code changed. Use project-native
   tests and sanitized fixtures. Then run the affected test projects and
   record results in specs/<feature-folder>/validation.md.
   ```

**Verification — pass before live acceptance:** The affected tests pass and both host registrations build. Sample vehicles map to the expected static routes and modes. An unavailable source remains distinguishable from a successful empty feed. Generic-only work records that this section's adapter creation was unnecessary, with integration checks still required.

## 6. Map, city picker and visitor copy

**Outcome:** A visitor can find the city, see its transit core, and understand what they are hearing.

1. Add the downtown/transit-dense starting coordinate to `_cityCenter` in `src/Client/ChefKnifeStudios.TransitJazz.Client.WebApp/Pages/TransitMap.razor.cs`. Check latitude/longitude ordering against existing entries.

2. Use the audio copy skill after the city constant exists:

   ```text
   $create-audio-overlay-paragraphs Write and wire the unlock overlay for
   <Agency> in <City>. Use the measured mode coverage and limitations in
   docs/city-compat/<agency-slug>.md. Explain the live transit music experience
   accurately, including inferred positions if applicable. Add the resource
   entries and the AudioUnlockOverlay city-prefix switch mapping.
   ```

3. Add the companion InfoFab paragraph and city-prefix switch. Both components use `src/Client/ChefKnifeStudios.TransitJazz.Client.Shared/Resources/RouteFilterResources.resx`, but their prefix families differ: for example, `TtcAudioOverlay…` versus `TtcOverlay…`. Add any required attribution. Avoid promising real GPS positions for synthesized trains or modes omitted from the feed.

4. Rebuild and inspect the rendered picker, map, unlock overlay, InfoFab and Stats city label. Check that authored `<strong>` markup renders as bold rather than literal tags, and that all resources resolve.

**Verification — pass before release testing:** Selecting the city updates the hash, reloads into the correct city and centers the map appropriately. A direct `/#<city-slug>` visit works. Text accurately describes included modes, displays cleanly on mobile and desktop, and remains legible in both themes. Human listening confirms the intended audio experience after unlock.

## 7. Local end-to-end verification

**Outcome:** Evidence that the complete pipeline works before production is changed.

1. Start the WebAPI and WebApp using their current project launch profiles. Inspect the local client API/SignalR destinations first and ensure they point at the local backend. Supply local database and agency credentials privately. Avoid running a second standalone worker against the same pipeline while the WebAPI-hosted worker is active.

2. Use `mj-api` against the **explicit local base URL**. Its default target is production and its documented local port may lag launch settings. Discover current endpoint parameters from code/OpenAPI, then query the new city's route catalog and representative shapes. Require city-specific evidence; global readiness or another city's loaded keys does not prove this city loaded.

3. Observe several update cycles during service hours. Compare source vehicles, decoded vehicles, matched routes, publication and visible motion. Confirm vehicles follow plausible streets/tracks and produce sound after audio unlock. If movement is absent, investigate freshness, route joins and geometry before changing musical settings.

4. Verify city switching, direct links, route filters, reconnect behavior and existing-city regressions. Check Stats availability and city-local time semantics. Newly collected history needs completed hours; show pending/unavailable honestly rather than expecting immediate historical totals.

5. Run the solution build and the affected existing test projects. Record commands, outcomes, sample routes, observation times and browser/listening results in `validation.md`. For database tests, follow their documented isolated database setup.

**Verification — pass before release preparation:** Static loading, feed decoding, route matching, publication, map motion and audible output all have evidence. Existing cities still function. No unresolved authentication, quota, stale-data or route-matching failures remain. Failed checks return to the relevant earlier section; do not substitute a green build for live acceptance.

## 8. Production configuration and release preparation

**Outcome:** A reviewed release target, credential configuration, deployment sequence and rollback plan.

1. Re-read [.github/workflows/server.yml](../.github/workflows/server.yml), [client.yml](../.github/workflows/client.yml), [client-marta.yml](../.github/workflows/client-marta.yml) and [the infrastructure notes](../bicep/README.md). Confirm the actual backend used by `transitjazz.com` from client production configuration. Resource names containing `dev` are not sufficient evidence that a target is non-production.

   At this guide's verification date:

   | Workflow | Trigger/target |
   | --- | --- |
   | Server CI/CD | Push to `main` or `059-reported-vehicles`; deploys a SHA-tagged WebAPI image to `marta-jazz-dev-ca-server` in `marta-jazz-dev-rg`, gated by GitHub environment `server-dev` |
   | TransitJazz Client CI/CD | Push to `main` or `059-reported-vehicles`; publishes `transitjazz.com` using `AZURE_STATIC_WEB_APP_TOKEN_2` |
   | MartaJazz Client CI/CD | Push to `deploy/marta-jazz`; publishes the separate `martajazz.com` site |

2. For protected feeds, provision the agency credential in the target server's secret store and bind a secret reference to the exact environment variable expected by the adapter. Use the existing Container App `secretRefs`/`envVars` infrastructure pattern or the approved Azure portal process. Record secret names/references only. Verify managed-identity access where Key Vault references are used. A GitHub Actions secret alone is not a runtime environment variable.

3. Review deployed configuration overrides, history selectors and collection ownership. Ensure the new city will receive its intended configuration after deployment. Keep one capture owner per city where the statistics contract requires it; account for overlapping revisions during rollout. If infrastructure changes are needed, review a targeted Bicep validation/what-if before applying them.

4. Determine whether any schema change is actually required. A normal city configuration addition should not need a new migration. If the release contains pending migrations, follow [the database migration runbook](../specs/055-database/quickstart.md#build-and-verify-the-schema), apply and verify them from an allowed machine for the exact release SHA, and only then approve server deployment. Historical feature flags have separate release prerequisites.

5. Record the current healthy server image SHA/revision and client release, the intended new SHA, rollout order and rollback owner. Server and client workflows run independently; the client has no matching server approval gate in the checked-in workflow. Arrange a backend-first release or an explicit client deployment hold before pushing so the picker cannot advertise an unavailable city. If splitting releases, keep backend city registration and its required Stats resources together, then release the visible client entry after backend verification.

6. Review the complete diff and evidence with the human release owner. The AI leaves changes uncommitted. The human commits and decides when to push/merge; pushing a deployment branch can immediately trigger publication. Record any required release approval separately from routine local implementation.

**Verification — pass before deployment:** The actual production targets and triggers are confirmed; runtime credential references resolve without exposing values; migrations are either verified or explicitly unnecessary; backend/client sequencing is established; the previous healthy releases and rollback steps are recorded. Local acceptance is complete and the human has reviewed the exact release changes.

## 9. Deploy the server and client

**Outcome:** The reviewed version is running on the production backend and public site.

1. Have the release owner push/merge the reviewed server release through the confirmed workflow. Inspect the build-and-push result and verify its image tag matches the intended commit SHA. If the workflow is waiting at `server-dev`, complete the schema checks from Section 8 before approving it.

2. Watch the server deployment through revision readiness and traffic assignment. The server readiness endpoint is `/health/ready`; allow for cold static loading. Verify the new city's catalog and representative geometry as well as global health. Check that the active revision uses the intended image and credential bindings.

3. Use `transitjazz-logs` for bounded, read-only production evidence and `grafana` for metrics. Check new-city fetch/decode results, route matching, publication and existing-city behavior. Resolve errors before exposing the new picker entry.

   ```text
   $transitjazz-logs Verify the production rollout of <city-slug> for release
   <commit-sha> from <UTC-start> to <UTC-end>. Check feed failures, static
   readiness, route matching and publish outcomes, plus existing-city
   regressions. Use bounded read-only queries; redact credentials and raw
   vehicle identities. Save evidence in specs/<feature-folder>/validation.md.
   ```

4. Release the reviewed TransitJazz client through `client.yml` after backend acceptance. Verify the workflow artifact and deployed site correspond to the intended client commit. Updating the separate MartaJazz site is an explicit scope decision, not an automatic part of adding a TransitJazz catalog city.

5. If the new release fails readiness, authentication, publication or existing-city checks, stop rollout. Restore the recorded healthy server image/revision and client release using the established deployment process; remove or revert the new client entry if necessary. Preserve database data and use the migration runbook's compatibility guidance rather than improvising destructive schema rollback. Recheck existing-city health after restoration.

**Verification — pass before calling the release deployed:** Both required workflow runs succeeded, intended revisions/artifacts are serving traffic, the new city's production routes load, and production worker evidence shows successful processing/publication. Workflow success alone is insufficient. Record release SHAs, workflow links, active revision and UTC deployment time.

## 10. Verify the city in production and close the release

**Outcome:** The city's feed is deployed and usable by real visitors.

1. Open `https://transitjazz.com/#<city-slug>` in a fresh browser session. Verify the city picker, initial map center, route geometry, live vehicles, unlock copy and InfoFab. Unlock audio and listen; check several feed updates during service hours.

2. Switch to existing cities and back. Verify route filtering and reconnection, and repeat a mobile-size and theme check. Confirm the production client is using the intended backend, not a local or stale endpoint.

3. Check the new city's production telemetry across the agreed observation window. Include multiple source updates and, when claiming historical-statistics acceptance, the required completed-hour window. Use `transitjazz-db` for read-only history checks if needed. Record missing/partial history accurately; never backfill invented vehicle populations.

4. Complete `validation.md` with the public city URL, backend/client SHAs, workflow links, UTC verification times, measured results, credential owner/renewal reference and any explicitly accepted limitations. Mark the feature tasks complete only for checks actually performed. Schedule any longer soak or credential renewal follow-up with an owner and due date.

**Final verification — city inclusion is complete when:** The public city link works, production static and realtime data agree, vehicles move and generate audible output, copy matches actual coverage, existing cities remain healthy, and release evidence identifies the running versions. The final record should say **“Deployed and verified”** only after these checks pass; otherwise record the specific failed or pending check.
