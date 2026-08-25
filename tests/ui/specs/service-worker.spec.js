// What the service worker is allowed to keep, and what it must not.
//
// The worker decides on one signal: `Cache-Control: immutable`, which MapStaticAssets sets on a
// fingerprinted route and on nothing else. That makes the assertion worth writing down not "the
// stylesheet is cached" but the two either side of it — that a page is never cached, because
// markup is served `no-cache` and an admin's copy of /stats carries the minutes #98 keeps from
// visitors; and that a script served `no-cache` is passed over even though it is the same kind of
// request as one that is kept. Those two are the policy. The stylesheet is just the payoff.
import { test, expect } from '../fixtures.js';
import { VISITOR_STATE } from '../playwright.config.js';
import { gotoRendered } from '../helpers.js';

test.use({ storageState: VISITOR_STATE });

const CACHE = 'ff-immutable-assets';

/** Every URL the worker has kept. Empty rather than throwing when it has not run yet. */
function cachedUrls(page) {
  return page.evaluate(async (name) => {
    if (!(await caches.has(name))) return [];

    const cache = await caches.open(name);
    return (await cache.keys()).map(request => request.url);
  }, CACHE);
}

test('the worker keeps the fingerprinted assets, and nothing that is not one', async ({ page }) => {
  // The first load is what registers the worker: its own asset requests were already in flight
  // before there was anything to intercept them, so this fills nothing.
  await gotoRendered(page, '/stats');
  await page.evaluate(() => navigator.serviceWorker.ready);

  // A second document rather than a reload. Chromium can serve a reloaded page's subresources from
  // the in-memory cache of the document it is replacing, which fires no fetch event at all — the
  // worker would be bypassed and this test would fail on a worker that works.
  const controlled = await page.context().newPage();
  await gotoRendered(controlled, '/stats');

  // Polled, because the worker writes to the cache without the page waiting on it: the entries
  // land shortly after the response the page has already rendered from.
  await expect.poll(async () => (await cachedUrls(controlled)).length).toBeGreaterThan(0);

  const urls = await cachedUrls(controlled);

  // Not the page, and not any other navigation. This is the one that matters.
  expect(urls.filter(url => !/\.(css|js|woff2?|png|svg)$/.test(new URL(url).pathname))).toEqual([]);
  expect(urls.filter(url => new URL(url).pathname === '/stats')).toEqual([]);

  // MapStaticAssets writes the hash in before the extension, so what is kept is never the plain
  // route it was authored as. MudBlazor's stylesheet is 611KB and most of the point of the cache;
  // it is only fingerprinted because App.razor asks for it through Assets[].
  expect(urls.some(url => /\/app\.[a-z0-9]+\.css$/.test(url)), 'app.css was not cached').toBe(true);
  expect(urls.some(url => /\/MudBlazor\.min\.[a-z0-9]+\.css$/.test(url)),
    'MudBlazor.min.css was not cached — is it still referenced through Assets[]?').toBe(true);

  // The control. js/pwa.js is a same-origin script on every page, exactly like the ones above, and
  // is passed over for the single reason that it is served without a fingerprint and so without
  // `immutable`. If this ever appears, the worker has stopped reading the header and started
  // guessing — which is the failure that would put a page in there too.
  expect(urls.filter(url => new URL(url).pathname === '/js/pwa.js')).toEqual([]);

  await controlled.close();
});
