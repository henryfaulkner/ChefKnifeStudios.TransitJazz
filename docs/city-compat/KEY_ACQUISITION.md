# API Key Acquisition — Key-Gated Agencies

Companion to the per-agency reports in `docs/city-compat/`. Those reports establish
*that* an agency is key-gated; this document covers *how a human obtains the key*, and —
more importantly — **whether obtaining it is actually sufficient.**

For several agencies it is not. Three distinct blockers sit behind the uniform
"KEY-GATED" label the compat reports apply:

1. **A credential gap** — get the key, set config, done.
2. **A code gap** — the key exists but TransitJazz cannot currently send it.
3. **A rate-limit or format gap** — the key works and the app still can't use the feed.

The compat reports' stock phrase *"config-only fix once a key exists"* is **too
optimistic for 4 of the 9 agencies below.** Read the verdict column before planning work.

> **Nothing here was acted on.** No account was registered, no form submitted, no terms
> accepted, and no key acquired. Every procedure below requires a human. Facts marked
> `UNVERIFIED` could not be confirmed from a first-party source — they are gaps, not
> guesses, and are called out individually.

**Researched 2026-08-29.** Live probes are timestamped; agency terms and rate limits
change without notice. Re-verify before committing to an onboarding spec.

---

## Summary

| Agency | City | Auth | Get key → works? | Blocker |
|---|---|---|---|---|
| **Sound Transit** | Seattle | `key` query | **Key may be unnecessary** | Keyless endpoint live today |
| **OC Transpo** | Ottawa | `?subscription-key=` | **Yes — config only** | None material |
| **TriMet** | Portland | `appID` query | **Yes — config only** | None material |
| **MTS** | San Diego | `key` query | **Yes — config only** | 5–7 business day wait |
| **SFMTA** | San Francisco | `api_key` query | **No — code fix** | URL-building defect |
| **STM** | Montréal | `apiKey` **header** | **No — code fix** | No header support |
| **NTA / TFI** | Dublin | `?subscription-key=` | **No — rate limit** | 1 call / 60s per token |
| **TransLink** | Vancouver | `apikey` query | **No — rate limit** | 1,000 requests/day |
| **CTA** | Chicago | two separate keys | **No — wrong format** | Publishes no GTFS-RT |

**Recommended order if onboarding several:** start MTS first (longest manual wait), then
OC Transpo and TriMet (config-only, generous limits). Sound Transit needs no key today.
Treat SFMTA and STM as small code tasks. Escalate NTA and TransLink as product decisions.
CTA is an adapter project, not a signup.

---

## Code changes required

Two agencies are blocked on TransitJazz code, not on the agency. Both are small and both
live in the same file.

### 1. No header support — blocks STM

`CityConfig` (`src/Server/.../Cities/CityConfig.cs:14-15`) exposes only:

```csharp
public string? ApiKeyEnvVar { get; set; }
public string ApiKeyQueryParam { get; set; } = "api_key";
```

There is **no header field at all**, and `GtfsRtCity.FetchFeedAsync`
(`Cities/GtfsRtCity.cs:61`) sets no auth header. STM requires the key in an `apiKey`
request header, so a valid STM key cannot currently be sent.

**Fix:** an additive nullable `ApiKeyHeader` on `CityConfig` plus a
`request.Headers.Add(...)` branch in `FetchFeedAsync`. A few lines in one existing class —
**not** a bespoke `ITransitCity`.

### 2. URL-building defect — blocks SFMTA

`GtfsRtCity.cs:61` builds the request URL by blind concatenation:

```csharp
var requestUrl = apiKey is not null ? $"{url}?{config.ApiKeyQueryParam}={apiKey}" : url;
```

It unconditionally appends `?`. **No currently-configured URL in `appsettings.json`
contains a query string, which is why this has never fired.** SFMTA is the first agency
that would hit it: 511's `agency` parameter is mandatory, so the base URL must be
`http://api.511.org/transit/vehiclepositions?agency=SF`, producing:

```
http://api.511.org/transit/vehiclepositions?agency=SF?api_key=KEY
```

The second `?` is not a delimiter. `agency` parses as the literal `SF?api_key=KEY`, the
key is never seen, and the failure presents as an authentication error that looks like a
bad key — nasty to debug cold.

**Fix:** select the separator on whether the base URL already contains `?` (append `&`
when it does), or compose via `UriBuilder`. No `CityConfig` setting can work around it.

### Also note

`GtfsStaticLoader.cs:172` reads `ApiKeyEnvVar` on the **static** side independently. A
key-gated *static* feed is a separate code path from the realtime one. None of the nine
agencies below need this today (all static feeds are keyless), but it matters for future
agencies.

---

## Config-only agencies

### OC Transpo (Ottawa, ON) — strongest candidate in this document

- **Signup:** https://nextrip-public-api.developer.azure-api.net/signup
- **Feed:** `https://nextrip-public-api.azure-api.net/octranspo/gtfs-rt-vp/beta/v1/VehiclePositions`
- **Config:** `ApiKeyQueryParam = "subscription-key"`

**Procedure**
1. Sign up at the developer portal (email, name, password) — *human action*.
2. Confirm via emailed verification link — *human action*.
3. Sign in → **Products** → subscribe to the product exposing GTFS-RT.
4. Read the key from **Profile** (Azure APIM issues primary + secondary keys).

**Turnaround:** self-serve. Whether the product auto-approves or needs staff approval is
`UNVERIFIED` — the products page is login-walled. Support: `octranspo-dev@ottawa.ca`.

**Auth mechanism — verified by live probe (2026-08-29).** The gateway's own challenge
names the header `Ocp-Apim-Subscription-Key` (the APIM default, un-renamed). But the
query-parameter form also works:

| Request | Response |
|---|---|
| no auth | `401 … missing subscription key` |
| `?zzz=1` (control) | `401 … missing subscription key` |
| `?subscription-key=INVALIDPROBE` | `401 … **invalid** subscription key` |

The flip from "missing" to "invalid" only occurs when the gateway parses a credential;
the control rules out a generic response change. **Config-only — no code change.**

**Terms** — [City of Ottawa Open Data Terms of Use](https://www.octranspo.com/en/plan-your-trip/travel-tools/developers/dev-terms)
- **Commercial use explicitly allowed** — "for both non-commercial and commercial purposes."
  The most permissive of the nine.
- **Prohibited:** circumventing or modifying API keys; transferring keys to another user;
  disrupting City services.
- **Rate limits:** no published numeric quota. Enforcement is discretionary and
  performance-based; developers are directed to cache and minimize requests. Any
  APIM product-level quota is `UNVERIFIED` (login-walled).
- **Attribution:** governed by the City of Ottawa Open Data Terms; exact wording
  `UNVERIFIED` — see caveat.

> ⚠️ **Source caveat:** `www.octranspo.com` returns HTTP 403 to all automated requests,
> and web.archive.org was unreachable. The terms above come from **search-engine-indexed
> excerpts, not a direct page read.** A human should open `/developers/dev-terms` and
> `/developers/best-practices` in a browser to confirm attribution wording before public
> deployment.

**Gotchas**
- The endpoint path contains `/beta/` — expect breaking changes, no stability guarantee.
- Vehicle Positions and Trip Updates may be separate products; confirm the subscription
  covers Vehicle Positions.
- Bus GPS updates every ~30s; polling faster yields no new data and only burns quota.

---

### TriMet (Portland, OR) — most generous rate limit

- **Signup:** https://developer.trimet.org/appid/registration/
- **Feed:** `http://developer.trimet.org/ws/V1/VehiclePositions`
- **Config:** `ApiKeyQueryParam = "appID"` — **case-sensitive, capital `ID`**

**Procedure**
1. Complete the registration form — *human action*. Fields: contact name, email, **year
   and month of birth** (an unusual COPPA-style age gate), application name, company
   (optional), notes (optional). No mandatory use-case description.
2. Acknowledge the Terms of Use.
3. Receive the AppID.

**Turnaround:** `UNVERIFIED`. No figure published on the registration, why-an-AppID, or
terms pages. The self-serve form shape suggests near-immediate issuance, but no agency
statement exists either way.

> A prior compat run recorded HTTP 403 from `developer.trimet.org`. That did **not**
> reproduce in this research pass — registration, terms, GTFS, and vehicle-locations
> pages all served successfully. The earlier 403 appears to have been transient WAF
> behavior.

**Terms** — [TriMet Developer Terms of Use](https://developer.trimet.org/terms_of_use.shtml)
- **Rate limit: 1,000,000 requests/day** by default — three orders of magnitude above
  TransLink. A continuous poller sits comfortably inside this. Higher limits via
  `labs@trimet.org`.
- **AppIDs are deleted after 6 months of inactivity.** Irrelevant for a running
  deployment; matters for a key registered ahead of launch.
- **Redistribution — the one ambiguity.** A site-wide clause prohibits "reproduction,
  modification, distribution, transmission, republication, display or performance …
  without the express written consent of TriMet," while the developer program separately
  grants "a limited, revocable license to use, reproduce, redistribute and display the
  Data in accordance with these terms." The developer grant is the more specific
  provision and governs API use, but "in accordance with these terms" is
  self-referential and leaves less clearance than 511's or MTS's language.
  **A confirmation email to `labs@trimet.org` is cheap insurance before public deploy.**
- **No general attribution requirement** for transit data (an attribution rule exists
  only for Public Art Content — irrelevant here).
- **Commercial use:** not expressly restricted.

**Gotchas**
- `appID` capitalization is load-bearing; lowercase `appid` fails.
- V1 GTFS-RT and V2 JSON are different APIs sharing one AppID.
- Endpoints are documented over **`http://`**. Verify HTTPS before relying on it — a raw
  `HttpClient` will not upgrade automatically.

---

### MTS (San Diego, CA) — cleanest license, longest wait

- **Signup:** https://www.sdmts.com/business-center/app-developers/real-time-data
- **Feed:** `https://realtime.sdmts.com/api/api/gtfs_realtime/vehicle-positions-for-agency/MTS.pb`
- **Config:** `ApiKeyQueryParam = "key"`

**Procedure**
1. Complete the "Real Time API Request" form — *human action*. Fields: name, company/app
   name, email, and **project type** — *Commercial/Public Use App* vs *Personal Use/School
   Project*. A publicly deployed TransitJazz is the **Commercial/Public Use** category
   regardless of being free.
2. Agree to the Developer License Agreement.
3. Wait for MTS staff to fulfil manually and email the key.

**Turnaround: 5–7 business days**, quoted verbatim by the agency. **The longest lead time
of the nine — start this one first** if onboarding several cities together.

**Credential:** a GUID/UUID (8-4-4-4-12 hex), passed as `key` (not `api_key`).

**Terms** — [MTS Developer License Agreement](https://www.sdmts.com/business-center/app-developers/terms-and-conditions)
- **Commercial use not restricted**; redistribution expressly in the grant. **No clause
  requires prior written permission before public redistribution.** The cleanest license
  of the nine.
- **Rate limits:** none published. MTS reserves the right to limit access rate without
  specifying a number. Concrete figure `UNVERIFIED`.
- **Branding:** MTS trademarks "may not be used in association with GTFS Data." Note this
  is a *prohibition on using marks*, not an attribution requirement — no affirmative
  attribution clause found.
- **Revocable** at any time without prior notice.

**Gotcha:** the parent [App Developers page](https://www.sdmts.com/business-center/app-developers)
is stale and still says MTS "hope[s] to share our real time information in the future,"
contradicting the child Real Time Data page. The feed exists and is keyed — use the child
page as authoritative.

---

## Sound Transit (Seattle, WA) — the key may be unnecessary

**Headline: a keyless GTFS-RT endpoint is live and consumable today.** This was verified
independently, twice, on 2026-08-29:

```
https://api.pugetsound.onebusaway.org/api/gtfs_realtime/vehicle-positions-for-agency/40.pb
```

| Probe | Result |
|---|---|
| no `key` parameter | **HTTP 200**, valid protobuf |
| `?key=BOGUSKEY123` | HTTP 200, **byte-identical** |
| `?key=TEST` | HTTP 200, byte-identical |

Payload is genuine GTFS-RT (`gtfs_realtime_version = "2.0"`), 43 vehicle entities:

| Field | Coverage |
|---|---|
| lat/lon | **100%** |
| `vehicle.timestamp` | 100% |
| `route_id` | **95.3%** |
| speed | 0% |
| bearing | 0% |

Live route IDs: `100232`, `100451`, `100479`, `2LINE`, `512`, `560`, `574`, `594` —
1 Line, 2 Line, and ST Express.

> ⚠️ **Read this before relying on it.** Identical responses across absent, valid, and
> bogus keys mean the gate is **currently unenforced, not officially public.** Sound
> Transit's own documentation still states a key is required. Treat as "works today,
> could start returning 401 without notice."
>
> **Recommendation:** build against the keyless URL to unblock immediately, *and* start
> the email request in parallel. The credential costs only calendar time, and the config
> already supports it (`ApiKeyQueryParam = "key"`).

### Two corrections to `sound-transit.md`

**1. The static source in that report is wrong for onboarding.** `40_gtfs.zip` has 8
routes and covers only 2 of the 8 live RT ids. Use the consolidated feed:

```
https://gtfs.sound.obaweb.org/prod/gtfs_puget_sound_consolidated.zip
```

411 routes, **8/8 RT id coverage**.

**2. A `RailRouteIdMap` entry is load-bearing, not polish.** Against the worker's index
key (`route_short_name ?? route_id`) only 4/8 match; against static `route_id`, 8/8. The
four misses are `100232`→`522`, `100451`→`556`, `100479`→`1 Line`, `2LINE`→`2 Line`.
**Without the map, both Link light rail lines — the marquee service — land in
`"unknown"`.** This is the same config-only mechanism KCM already uses; zero new code.

### KCM's keyless-S3 trick does NOT generalize

`kcm.md` calls the OneBusAway key a "red herring" for King County Metro. That was tested
and is **genuinely KCM-only**:

| Probe | Result |
|---|---|
| `kcm-alerts-realtime-prod/vehiclepositions.pb` | 200 (KCM's working feed) |
| `st-service-alerts-prod/vehiclepositions.pb` | **403 AccessDenied** |
| `st-service-alerts-prod/alerts.pb` | 200 (alerts only, as expected) |
| `st-alerts-realtime-prod`, `soundtransit-alerts-realtime-prod` | **404 NoSuchBucket** |

The KCM feed decodes to zero Sound Transit identifiers. The ST win comes from the
OneBusAway protobuf passthrough instead.

### Key request (if pursued in parallel)

**Email:** `oba_api_key@soundtransit.org` — verified on Sound Transit's
[OTD developer resources](https://www.soundtransit.org/help-contacts/business-information/open-transit-data-otd).

1. Read the [Transit Data Terms of Use](https://www.soundtransit.org/help-contacts/business-information/open-transit-data-otd/transit-data-terms-use)
   in full — *a human must accept these*.
2. Email `oba_api_key@soundtransit.org` with: contact first and last name, email address,
   and explicit acknowledgement of the Terms of Use.
3. Recommended additions: app name and public URL, non-commercial framing, and the
   polling interval.
4. Store the key as a secret referenced by `ApiKeyEnvVar` — never committed.

**Turnaround — conflicting figures.** Sound Transit's own page states **20 business
days**. [OneBusAway's Puget Sound wiki](https://pugetsound.onebusaway.org/wiki/Developers)
says **two**. Plan against the agency's 20-day figure; the shorter number may be stale.

**Terms** — covers ST, KCM, Pierce Transit, Community Transit, Intercity Transit,
Everett Transit, Seattle Streetcars, WSF.
- **Commercial use permitted** — "free to use the Data in any way you choose."
- **Naming restriction:** no agency mark or name "in the name of a business or
  application." A name like *SoundTransitJazz* would violate this; a neutral name with
  attribution elsewhere is the compliant shape.
- **Redistribution — sharpest clause:** "You agree to provide these Terms to all users
  who receive the Data from you." TransitJazz pushes live positions to browsers over
  SignalR, which is arguably provision of Data. **Surfacing a terms link in the
  InfoFab/overlay copy is the low-cost mitigation.**
- **No modification:** "not change the Data as published and to use the most current Data
  available." The app derives musical events rather than republishing altered feed data,
  which reads as compliant — worth a human confirmation.
- **Usage metrics** must be provided to Sound Transit on request.
- **Rate limits:** `UNVERIFIED` — none published in the Terms, OTD pages, or OneBusAway
  REST docs. Poll conservatively.

**Gotchas**
- Link lines are `route_type=0` (tram), not `1`. The classifier at `GtfsStaticLoader.cs`
  maps 0/1/2 all to Rail, so this is fine — but it contradicts the loose assumption that
  Link is `route_type=1`.
- Speed and bearing are **0%** — fully absent, not sparse. Rules out speed-dependent
  audio behavior.
- **Sounder absent from the sample.** `SNDR_EV`/`SNDR_TL` did not appear. Likely
  schedule-dependent — Sounder is weekday-peak commuter rail and the sample was taken on
  a Saturday. **Re-sample on a weekday morning before concluding it is unsupported.**

---

## Blocked on TransitJazz code

### SFMTA / Muni (San Francisco, CA) — via 511 SF Bay

- **Signup:** https://511.org/open-data/token
- **Feed:** `http://api.511.org/transit/vehiclepositions?api_key=[key]&agency=SF`
- **Config:** `ApiKeyQueryParam = "api_key"` (the existing default) — **but see the code
  defect above; config alone will not work.**

**Procedure**
1. Complete the token form — *human action*. Fields: first name, last name, email, and a
   Terms & Conditions checkbox. **No organization name, no use-case description** — the
   lightest signup of the nine.
2. Verify the email address. Per the [511 FAQ](https://511.org/about/faq/open-data):
   "Once your email address is verified, a key will be issued to you."

**Turnaround:** effectively instant, gated only on email verification.

**Terms** — [511 Data Disseminator Agreement](https://511.org/sites/default/files/pdfs/511_Data_Agreement_Final.pdf)
- **Rate limit: 60 requests per 3,600 seconds (one per minute) per token** — restrictive
  but workable for vehicle positions, and **explicitly negotiable**: request an increase
  via `transitdata@511.org` with your use case and justification. (The transit data page
  names a different address and says not to include your key; the two published contacts
  disagree — the FAQ's `transitdata@511.org` is the more specific instruction.)
- **Redistribution expressly licensed** — a "nonexclusive, royalty-free, worldwide,
  non-transferable license to use, sublicense, copy, distribute, and store the Provided
  Data … and to make the Provided Data available to end users through their services,
  sites or applications." **No prior written permission needed.**
- **One commercial restriction:** data "shall not be sold as received on a standalone
  basis." TransitJazz transforms positions into a soundscape and does not resell the raw
  feed — does not bite.
- **Attribution required**, crediting 511 as the source.
- **Third-party trademark bar:** you may not use SFMTA's own marks or logos without
  separate permission from that agency. **You may show the data, but not the Muni worm.**
- **Sublicensee ambiguity:** disseminators must "secure written acceptance of these terms
  … from prospective sublicensees." Whether end users of a public web app are
  "sublicensees" is unclear; in practice consumer apps are treated as the end-user tier.

> **Do not read the general [511.org site Terms](https://511.org/about/terms) in
> isolation** — they read as prohibiting redistribution, but carve out the developer
> program explicitly. The Data Disseminator Agreement *is* that authorization.

---

### STM (Montréal, QC)

- **Signup:** https://portail.developpeurs.stm.info/apihub/
- **Feed:** `https://api.stm.info/pub/od/gtfs-rt/ic/v2/vehiclePositions`
- **Config:** requires the new `ApiKeyHeader` field — **see Code changes above.**

> ⚠️ **Stale-URL warning:** the older host `developpeurs.stm.info` (still linked from most
> search results and blog posts) **no longer resolves** — confirmed via `nslookup`: no A
> or AAAA record. Only `portail.developpeurs.stm.info` resolves. Cite only the `portail.`
> host.

**Procedure**
1. Create an account at the portal — *human action*. This is **Broadcom Layer7 API Hub**,
   not Azure APIM, so the flow differs from NTA and OC Transpo.
2. Verify by email — *human action*.
3. **Home → Applications → create a new application.** The key is issued **per
   application**, not per account.
4. Request/attach the GTFS-realtime API to that application.
5. Read the key from the application's **Authentication & Credentials** section.

**Turnaround:** `UNVERIFIED`. Layer7 supports both auto-issue and admin-approved
registration; STM does not publish which mode it uses and the pages are login-walled.
Questions: `dev@stm.info`.

**Header name: `apiKey`** — corroborated by two independent sources:
[Transitland's feed record](https://www.transit.land/feeds/f-f25d-socitdetransportdemontral~rt)
carries structured auth metadata (type `header`, name `apiKey`), and working client code
passes `{"accept": "application/x-protobuf", "apiKey": "<key>"}`. HTTP header names are
case-insensitive per RFC 7230, so `.NET`'s `Headers.Add("apiKey", …)` is safe despite
STM's prose sometimes showing lowercase `apikey`.

**Query parameter: `UNVERIFIED`, assume NOT supported.** STM's endpoint returns a flat
`HTTP 400 Invalid API Key` with no `WWW-Authenticate` for *every* input — no auth, bad
header, `?apiKey=`, `?api_key=`. The response never varies, so the differential technique
that worked on the APIM gateways yields no signal. Documentation consistently describes a
mandatory header and Transitland records the auth type as `header`. Confirm with
`dev@stm.info` if a query form would avoid the code change.

**Terms**
- **Licence: CC-BY 4.0 (Québec)** — permits commercial use and redistribution.
- **Attribution:** authorship must be attributed to the Société de transport de Montréal.
- **Rate limits:** `UNVERIFIED` — no published quota; Layer7 per-application quotas may be
  visible only inside the portal.

**Gotchas**
- Keys are **per-application**; a second deployment may need its own.
- **Bus and métro only** — STM's realtime vehicle positions may not include métro train
  positions in the same feed. Verify with `mj-gtfs` before onboarding.
- The 400-for-everything behavior makes misconfiguration **hard to diagnose**: a wrong
  header name, wrong casing on a non-compliant proxy, and an expired key all produce the
  identical `Invalid API Key`.

---

## Blocked on rate limits

These two agencies issue keys that work — and still may not support a live-motion
soundscape. **These are product decisions, not engineering tasks.**

### NTA / Transport for Ireland (Dublin)

- **Signup:** https://developer.nationaltransport.ie/signup
- **Feed:** `https://api.nationaltransport.ie/gtfsr/v2/Vehicles`
- **Config:** `ApiKeyQueryParam = "subscription-key"` — **config-only, no code change**

**Auth mechanism — verified by live probe (2026-08-29).** The gateway challenge renames
the header to `x-api-key`:

```
WWW-Authenticate: AzureApiManagementKey realm="https://api.nationaltransport.ie/gtfsr",
                  name="x-api-key", type="header"
```

But the **query parameter retains the APIM default** `subscription-key`, and it works:

| Request | Response |
|---|---|
| no auth | `401 … missing subscription key` |
| `?zzz=1` (control) | `401 … missing subscription key` |
| `?x-api-key=INVALID` | `401 … missing subscription key` (param name rejected) |
| `?subscription-key=INVALIDPROBE` | `401 … **invalid** subscription key` |

> This **corrects `tfi.md`**, which concluded a header-injection code change might be
> needed. It is not — the query-param form is accepted. Dublin is config-only *on the
> auth axis*.

**Procedure**
1. Sign up at the developer portal — *human action*.
2. Confirm via emailed verification link — *human action*.
3. Sign in → **Products** → subscribe to the **GTFS-Realtime** product.
4. Retrieve the key from **Profile** (primary + secondary tokens issued).

**Turnaround:** self-serve APIM; auto-approval vs manual approval `UNVERIFIED`.
Support: `apisupport@nationaltransport.ie`.

**Terms** — [NTA Fair Usage Policy](https://developer.nationaltransport.ie/usagepolicy)
- **Licence:** CC-BY 4.0.
- **🚩 Rate limit — the real blocker: "Each Token will be restricted to calling the GTFS
  Real Time API once every 60 seconds."** TransitJazz's live-vehicle loop polls far more
  often. At a 60-second cadence, vehicle dots move in minute-long jumps and the
  interpolation and soundscape model degrade badly.
  - Two tokens are issued per user, which at best halves the interval to ~30s — still
    coarse, and **alternating tokens to defeat a per-token limit is arguably
    circumvention.** Not recommended without NTA's explicit blessing.
  - An older policy cited 5,000/day; the current policy replaces it with the 60s rule.
- **Commercial use:** prohibits "using the GTFS Data primarily for profit without adding
  significant value." A free public art project plausibly clears this, but it is a
  judgement call.
- **Attribution required:** the NTA's name, a link to the source, and an "as is" statement.

**Additional gotcha:** the feed covers **Dublin Bus, Bus Éireann, and Go-Ahead Ireland
only — no Luas (tram) or DART/Irish Rail vehicles.** This narrows Dublin's scope
considerably beyond what `tfi.md`'s static analysis implied.

**Bottom line:** resolve the 60-second limit with NTA before writing an onboarding spec.
Key acquisition is not the gate here.

---

### TransLink (Vancouver, BC)

- **Signup:** https://developer.translink.ca/Account/Register
  (the portal root redirects to a marketing page; `/Account/Register` still serves the form)
- **Feed:** `https://gtfsapi.translink.ca/v3/gtfsposition?apikey=[key]`
- **Config:** `ApiKeyQueryParam = "apikey"` — config-only *on the auth axis*

**Procedure**
1. Complete the registration form — *human action*. Fields: username (alphanumeric,
   6–20 chars), full name, email + confirm, password + confirm, and a **purpose of using
   the API** field. No organization field.
2. Accept the Open API Terms of Use.
3. Retrieve the key from the portal account.

One key covers all three endpoints (`gtfsrealtime`, `gtfsposition`, `gtfsalerts`).

**Turnaround:** `UNVERIFIED`. No quoted fulfillment time; the self-serve form implies
immediate issuance, but the written-notice clause below suggests a manual gate may exist
in practice.

**Terms** — [TransLink Open API Terms of Use](https://www.translink.ca/about-us/doing-business-with-translink/app-developer-resources/terms-of-use)
- **🚩 Rate limit — likely disqualifying: "Your API Key will authorize you to offer a
  maximum of 1,000 requests per day."** That is **one request per ~86 seconds.** TransLink
  further "reserves the right, at any time after the API Key is issued, to limit the
  number of maximum requests in any one day." A live-vehicle experience at 86-second
  granularity is not the product.
- **Written notice before use:** "You must contact TransLink in writing and provide
  sufficient information … to identify you, your organization or company, who will be
  using the Data, where it will be distributed and whether the use of the data is for
  non-commercial or commercial purposes." A public deployment is squarely "where it will
  be distributed" — an affirmative obligation, not boilerplate.
- **Commercial use triggers renegotiation:** charging end users lets TransLink impose
  additional terms, fees, or tighter request limits. TransitJazz is free, so this does not
  bite today, but it constrains future monetization.
- **Mandatory attribution**, displayed prominently: *"Some of the data used in this
  product or service is provided by permission of TransLink. TransLink assumes no
  responsibility for the accuracy or currency of the Data used in this product or
  service."*
- **Termination** on ten (10) days written notice.

**Bottom line:** the 1,000/day cap should gate any onboarding decision. This **corrects
`translink.md`'s "config-only once obtained" framing**, which is accurate about the auth
mechanism and misleading about viability.

---

## Blocked on feed format — CTA (Chicago, IL)

> **Obtaining both CTA keys leaves TransitJazz exactly as unable to display Chicago
> vehicles as it is today.** The procedures below are documented as assigned, but they
> lead to feeds the application structurally cannot read. **Chicago is not a signup away.**

**Why a key does not help.** The worker consumes **GTFS-RT protobuf only** —
`GtfsRtCity.FetchFeedAsync` ends in `ProtoBuf.Serializer.Deserialize<FeedMessage>(stream)`
with no content negotiation and no alternate parse path. Per `cta.md`, CTA publishes **no
GTFS-RT protobuf at all** — only two proprietary legacy APIs, Bus Tracker and Train
Tracker, both XML/JSON over HTTP. Pointing a `CityConfig` at `getvehicles` with a valid key
feeds XML into a protobuf deserializer: a deserialization failure, not vehicles on a map.

**What CTA would actually require, beyond both keys:**

1. **Two net-new protocol adapter classes** (analogous to
   `RailRealtime/RailRealtimeAdapter.cs`) — one for Bus Tracker, one for Train Tracker.
2. **A bespoke `ITransitCity`** — CTA cannot ride the config-driven `GtfsRtCity` `else`
   arm that WMATA/MBTA/TTC/SEPTA/RTD use.
3. **A `CityConfig` that can hold two keys against two hosts** — it currently exposes a
   single `ApiKeyEnvVar`/`ApiKeyQueryParam` pair and cannot express this.
4. **Field-completeness measurement that has never been done** — `cta.md` marks required
   fields `UNASSESSED`; no live sample has ever been pulled, so viability is unproven even
   after adapters exist.
5. **Verification of Train Tracker's live-position contract** — whether it yields one
   coordinate per train.

**Key #1 — Bus Tracker** (account-gated; no public form)
1. Create and activate a Bus Tracker consumer account.
2. Sign in → **My Account** (upper right) → follow the link under **Developer API**.
3. Agree to the Terms of Service — *human action*.

- Exact application URL `UNVERIFIED` — it sits behind authentication.
  `https://www.transitchicago.com/developers/bustrackerapply/` returns **404**.
- Turnaround `UNVERIFIED`.
- **Rate limit: 100,000 transactions/day** (raised from 50,000 in April 2024).

**Key #2 — Train Tracker** (public form, entirely separate application)
Form: https://www.transitchicago.com/developers/traintrackerapply/
1. Read the Developer License Agreement, Terms of Use, and Trademark Guidelines.
2. Complete: first name, last name, address, email, phone, **purpose of use** (free text),
   and the mandatory agreement checkbox.
3. Submit — *human action*.

- Turnaround: "pretty quickly," no specific figure — `UNVERIFIED`.
- **Rate limit: 50,000 transactions/day.** IP-level DoS protection may trigger a temporary
  time-out on heavy single-IP traffic — relevant for a single-worker deployment.
- Both the service and its API are **beta**.

**Terms** — governs *all* CTA Data collectively, including the keyless static GTFS zip.
- **🚩 Purpose restriction — the sharpest clause:** the license is granted "for the sole
  purpose of assisting mass transportation (i.e., bus or rail) riders or in furtherance of
  promoting public transportation." **An artistic/musical visualization is a debatable
  fit.** This is a real, non-obvious risk deserving a human judgment call *before*
  investing adapter effort.
- **No implied affiliation** with CTA; constrains naming and branding.
- **No resale** of CTA Data separate from the application.
- **Caching permitted** to improve user experience, with reasonable currency efforts.
- **Revocable and unilaterally amendable** at any time.
- **Governing law:** Illinois; exclusive venue Cook County.

**The one clean result:** rail line codes (`Red`, `Brn`, `Blue`, `G`, `Org`, `Pink`, `P`,
`Y`) match static `route_id` verbatim, so line-key mapping would be free — *once adapters
exist.* Static GTFS is keyless and drop-in, though large (~68 MB zipped, ~400 MB unzipped).

**Gotchas**
- `transitchicago.com` blocks automated fetchers (403 without a browser User-Agent).
  Expect friction re-verifying these pages.
- The two keys have **entirely different acquisition models** — budget for two unrelated
  processes.
- Bus Tracker's documented endpoint is plain **`http://`** — a mixed-content and
  in-transit-secrecy concern for a key-bearing request from a deployed app.

---

## Handling keys

Every key above is a secret.

- Store as an environment variable referenced by `CityConfig.ApiKeyEnvVar`.
- **Never commit a key**, including in `appsettings.json`. Feature 012's FR-020 exists
  because a live Azure account key was once committed to this repo in
  `telemetry-query-tool/main.go`.
- Several agencies bind keys to a declared application (MTS, STM, TransLink) or forbid
  transferring them (OC Transpo). A second deployment may need its own key.
- Sound Transit requires usage metrics on request; TransLink and CTA can revoke at any
  time.

---

## Legal caveat

Terms summaries above are **readings of published agency documents, not legal advice.**
They were compiled to support engineering triage. Several involve genuine ambiguity that a
human should resolve before public deployment — specifically TriMet's redistribution
tension, 511's sublicensee definition, Sound Transit's provide-these-terms-to-users
clause, NTA's commercial-exploitation wording, and CTA's purpose restriction. Where a
clause could plausibly be read either way, this document says so rather than picking the
convenient reading.
