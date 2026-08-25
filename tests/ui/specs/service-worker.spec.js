// What the service worker is allowed to keep, and what it must not.
//
// It decides on one signal: `Cache-Control: immutable`, set on a fingerprinted route and nothing
// else. So the assertions that matter are the two either side of "the stylesheet is cached" — that
// a page never is, and that a script served `no-cache` is passed over despite being the same kind
// of request.
import { test, expect } from '../fixtures.js';
import { VISITOR_STATE } from '../playwright.config.js';
import { gotoRendered } from '../helpers.js';

test.use({ storageState: VISITOR_STATE });

const CACHE = 'ff-immutable-assets';

/** Every URL the worker has kept; empty rather than throwing before it has run. */
function cachedUrls(page) {
  return page.evaluate(async (name) => {
    if (!(await caches.has(name))) return [];

    const cache = await caches.open(name);
    return (await cache.keys()).map(request => request.url);
  }, CACHE);
}

test('the worker keeps the fingerprinted assets, and nothing that is not one', async ({ page }) => {
  // Registers the worker. Its own asset requests were already in flight, so this fills nothing.
  await gotoRendered(page, '/stats');
  await page.evaluate(() => navigator.serviceWorker.ready);

  // A second document, not a reload: Chromium can serve a reloaded page's subresources from the
  // replaced document's memory cache, firing no fetch event — failing on a worker that works.
  const controlled = await page.context().newPage();
  await gotoRendered(controlled, '/stats');

  // Polled: the worker writes without the page waiting, so entries land after the response.
  await expect.poll(async () => (await cachedUrls(controlled)).length).toBeGreaterThan(0);

  const urls = await cachedUrls(controlled);

  // Not the page, and not any other navigation. This is the one that matters.
  expect(urls.filter(url => !/\.(css|js|woff2?|png|svg)$/.test(new URL(url).pathname))).toEqual([]);
  expect(urls.filter(url => new URL(url).pathname === '/stats')).toEqual([]);

  // Never the plain route it was authored as. MudBlazor's stylesheet is 611KB and most of the
  // point of the cache, and is only fingerprinted because App.razor asks through Assets[].
  expect(urls.some(url => /\/app\.[a-z0-9]+\.css$/.test(url)), 'app.css was not cached').toBe(true);
  expect(urls.some(url => /\/MudBlazor\.min\.[a-z0-9]+\.css$/.test(url)),
    'MudBlazor.min.css was not cached — is it still referenced through Assets[]?').toBe(true);

  // The control: pwa.js is a same-origin script like the ones above, passed over only because it
  // is served without a fingerprint and so without `immutable`. If it appears, the worker has
  // started guessing — the same failure that would put a page in there.
  expect(urls.filter(url => new URL(url).pathname === '/js/pwa.js')).toEqual([]);

  await controlled.close();
});
