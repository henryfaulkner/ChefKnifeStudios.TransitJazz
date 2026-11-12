# Client experience synthetic check

`check.js` is a Grafana Cloud Synthetic Monitoring **k6 browser** script. Provision one Chromium browser check per supported deployment at a five-minute interval and test from the intended probe locations before relying on its alert. The repository contains the script only; no tenant check, location, schedule, alert, or probe spend has been configured here.

Configure runner-only variables:

- `TRANSITJAZZ_BASE_URL`: public application origin, without a trailing slash.
- `CLIENT_EXPERIENCE_SYNTHETIC_TOKEN`: a random token of at least 32 characters, configured as a secret in the check runner and as `ClientExperience__SyntheticIntakeToken` in the API environment. Never put it in the app shell, query string, or browser bundle.

On each run, the browser visits `/#atlanta`, verifies shell startup, route data, usable map initialization, and a confirmed live city join. The hidden `#client-experience-readiness` marker exposes only these coarse owner states. Failures emit `transitjazz_synthetic_app_readiness_failure`; browser initialization/configuration failures emit `transitjazz_synthetic_runner_failure` so alerts can keep app incidents separate from check execution problems.

The check intercepts `/api/client-experience/batch`, rewrites it to `/api/client-experience/synthetic/batch`, and adds the runner-held bearer credential only to the POST request. A browser CORS preflight, if required, is rewritten to the same synthetic route without the credential. The server assigns environment `synthetic`; the normal endpoint rejects browser-supplied environment fields. Enable `ClientExperience:SyntheticIntakeEnabled` only with a protected runner secret. The API option is off by default and requires the main intake to be enabled. Synthetic rows must be excluded from real-visitor dashboard cohorts and alert queries.

Before provisioning, verify the application's allowed origin includes the deployed app origin and the synthetic path is routed by the same public proxy. Then run an end-to-end check and confirm the credential is absent from browser assets, logs, Grafana labels, and acknowledgement bodies. A successful script check is not proof that a Grafana alert or contact point fired; exercise those separately in staging.

The script uses the documented `k6/browser` Chromium scenario and request routing API. See [Grafana's browser-check guide](https://grafana.com/docs/grafana-cloud/observe-and-act/testing/synthetic-monitoring/get-started/create-a-k6-browser-check/) and [k6 Page.route documentation](https://grafana.com/docs/k6/latest/javascript-api/k6-browser/page/route/).
