import { browser } from 'k6/browser';
import { check, fail } from 'k6';
import { Rate } from 'k6/metrics';

// These metrics separate a failed TransitJazz readiness assertion from a broken
// check runner/browser. Provision the check at a five-minute interval in Grafana.
const appReadinessFailure = new Rate('transitjazz_synthetic_app_readiness_failure');
const runnerFailure = new Rate('transitjazz_synthetic_runner_failure');

export const options = {
  scenarios: {
    browser: {
      executor: 'shared-iterations',
      options: { browser: { type: 'chromium' } },
    },
  },
};

function syntheticBatchUrl(requestUrl) {
  const target = new URL(requestUrl);
  if (!target.pathname.endsWith('/api/client-experience/batch')) {
    throw new Error('Unexpected client-experience intake URL.');
  }
  target.pathname = target.pathname.replace(
    /\/api\/client-experience\/batch$/,
    '/api/client-experience/synthetic/batch',
  );
  return target.toString();
}

export default async function () {
  const baseUrl = (__ENV.TRANSITJAZZ_BASE_URL || '').replace(/\/$/, '');
  const token = __ENV.CLIENT_EXPERIENCE_SYNTHETIC_TOKEN || '';
  if (!baseUrl || token.length < 32) {
    runnerFailure.add(true);
    fail('Runner configuration requires TRANSITJAZZ_BASE_URL and a 32+ character CLIENT_EXPERIENCE_SYNTHETIC_TOKEN.');
  }

  let page;
  try {
    page = await browser.newPage();
  } catch (error) {
    runnerFailure.add(true);
    throw error;
  }

  try {
    await page.route(/\/api\/client-experience\/batch(?:[?#].*)?$/, async (route) => {
      const request = route.request();
      const headers = request.headers();
      if (request.method() === 'POST') {
        headers.authorization = `Bearer ${token}`;
      }
      await route.continue({ url: syntheticBatchUrl(request.url()), headers });
    });

    let shellLoaded = false;
    try {
      await page.goto(`${baseUrl}/#atlanta`);
      await page.waitForFunction(
        () => document.readyState !== 'loading' && document.querySelector('#app'),
        { timeout: 60_000 },
      );
      shellLoaded = true;
    } catch {
      // A browser navigation that completes without the app shell is an application failure.
    }

    const shellCheck = check(shellLoaded, {
      'TransitJazz shell starts': (loaded) => loaded,
    });
    if (!shellLoaded) {
      appReadinessFailure.add(true);
      fail('TransitJazz shell did not start.');
    }

    let mapReady = false;
    let routesReady = false;
    let liveJoined = false;
    try {
      await page.waitForFunction(() => {
        const marker = document.querySelector('#client-experience-readiness');
        return marker?.dataset.routesReady === 'true'
          && marker?.dataset.mapReady === 'true'
          && marker?.dataset.liveJoined === 'true';
      }, { timeout: 90_000 });
      const marker = page.locator('#client-experience-readiness');
      routesReady = (await marker.getAttribute('data-routes-ready')) === 'true';
      mapReady = (await marker.getAttribute('data-map-ready')) === 'true';
      liveJoined = (await marker.getAttribute('data-live-joined')) === 'true';
    } catch {
      // Keep individual false checks below so the failing owner is visible in check output.
      const marker = page.locator('#client-experience-readiness');
      routesReady = (await marker.getAttribute('data-routes-ready').catch(() => 'false')) === 'true';
      mapReady = (await marker.getAttribute('data-map-ready').catch(() => 'false')) === 'true';
      liveJoined = (await marker.getAttribute('data-live-joined').catch(() => 'false')) === 'true';
    }

    check(routesReady, { 'Current-city route data loaded': (ready) => ready });
    check(mapReady, { 'Current-city map is usable': (ready) => ready });
    check(liveJoined, { 'Live city delivery joined': (ready) => ready });
    const ready = shellCheck && routesReady && mapReady && liveJoined;
    appReadinessFailure.add(!ready);
    if (!ready) fail('TransitJazz current-city readiness did not complete.');
  } catch (error) {
    if (String(error).includes('TransitJazz shell') || String(error).includes('TransitJazz current-city readiness')) {
      throw error;
    }
    runnerFailure.add(true);
    throw error;
  } finally {
    try {
      await page.close();
    } catch {
      runnerFailure.add(true);
    }
  }
}
