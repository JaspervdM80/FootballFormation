// The assertions that matter are the two either side of "the stylesheet is cached": that a page
// never is, and that a `no-cache` script is passed over despite being the same kind of request.
import { test, expect } from '../fixtures.js';
import { VISITOR_STATE } from '../playwright.config.js';
import { gotoRendered } from '../helpers.js';

test.use({ storageState: VISITOR_STATE });

const CACHE = 'ff-immutable-assets';

/** Empty rather than throwing before the worker has run. */
function cachedUrls(page) {
  return page.evaluate(async (name) => {
    if (!(await caches.has(name))) return [];

    const cache = await caches.open(name);
    return (await cache.keys()).map(request => request.url);
  }, CACHE);
}

test('the worker keeps the fingerprinted assets, and nothing that is not one', async ({ page }) => {
  // Registers the worker; its own requests were already in flight, so this fills nothing.
  await gotoRendered(page, '/stats');
  await page.evaluate(() => navigator.serviceWorker.ready);

  // A second document, not a reload: Chromium can serve a reload's subresources from the replaced
  // document's memory cache, firing no fetch event — failing on a worker that works.
  const controlled = await page.context().newPage();
  await gotoRendered(controlled, '/stats');

  // Polled: the worker writes without the page waiting on it.
  await expect.poll(async () => (await cachedUrls(controlled)).length).toBeGreaterThan(0);

  const urls = await cachedUrls(controlled);

  // Not the page, and not any other navigation.
  expect(urls.filter(url => !/\.(css|js|woff2?|png|svg)$/.test(new URL(url).pathname))).toEqual([]);
  expect(urls.filter(url => new URL(url).pathname === '/stats')).toEqual([]);

  // MudBlazor's 611KB stylesheet is most of the point, and is only fingerprinted because
  // App.razor asks for it through Assets[].
  expect(urls.some(url => /\/app\.[a-z0-9]+\.css$/.test(url)), 'app.css was not cached').toBe(true);
  expect(urls.some(url => /\/MudBlazor\.min\.[a-z0-9]+\.css$/.test(url)),
    'MudBlazor.min.css was not cached — is it still referenced through Assets[]?').toBe(true);

  // The control: same kind of request, passed over only for lacking `immutable`. If it appears,
  // the worker has started guessing — the failure that would put a page in there too.
  expect(urls.filter(url => new URL(url).pathname === '/js/pwa.js')).toEqual([]);

  await controlled.close();
});
