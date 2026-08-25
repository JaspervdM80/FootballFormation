// Caches the assets the app is built from, and nothing else.
//
// The whole policy is one line: **cache what the server itself calls immutable.** `MapStaticAssets`
// fingerprints every asset referenced through `@Assets[...]` in App.razor and serves that route
// with `Cache-Control: max-age=31536000, immutable`, while the un-fingerprinted route for the very
// same file gets `no-cache`. So the header is not a hint to weigh here — it is the build stating
// that this URL's bytes can never change, which is precisely the precondition cache-first needs.
//
// Reading it off the response is what keeps this file from needing maintenance:
//
//   * **No cache version, and none to forget.** A deploy re-fingerprints whatever changed, so the
//     new build asks for a different URL and the old entry is simply never requested again. There
//     is nothing to bust on activate.
//   * **Markup is never cached.** A rendered page is served `no-cache`, so it fails the test — and
//     with it goes the risk that an admin's copy of /stats is stored and handed to the next person
//     on a shared phone. Since #98 that page carries the playing-time card and the minutes a
//     visitor must not see, and a cache is the easiest place in the app to leak them by accident.
//   * **Getting it wrong is a missed optimisation, never a stale asset.** A file referenced
//     without `@Assets[...]` is served `no-cache` and stays on the network for good.
//
// This is deliberately *not* offline support. With only CSS, JS and fonts in the cache, every page
// still needs the network for its markup, and at a pitch with no signal it fails as it does today.
// Caching pages is a different feature with a harder problem in it — a cached *interactive* page
// is a dead shell, and the render mode that decides which is which is invisible from a URL. See
// issue #104, which is scoped to that and to nothing in this file.
//
// The fetch handler also has to exist at all for Android/Chrome to treat the app as installable,
// which is the only reason the pass-through version of this file existed.

const CACHE = 'ff-immutable-assets';

// Assets only, by request destination rather than by URL. A navigation is markup — the one thing
// that must never be answered from here — and `destination` is the browser telling us what the
// request is *for*, which no amount of path matching can work out. `/_blazor` negotiation is
// `empty` and falls out of this set for free.
const CACHEABLE = new Set(['style', 'script', 'font', 'image']);

// Fingerprinted entries are orphaned rather than replaced across a deploy — the new build asks for
// a different URL — so nothing here ever evicts itself. Capped and trimmed oldest-first instead;
// `cache.keys()` answers in insertion order.
const MAX_ENTRIES = 60;

self.addEventListener('install', () => self.skipWaiting());

self.addEventListener('activate', (event) => event.waitUntil((async () => {
    // Only caches under an older *name*, i.e. a previous shape of this file. Deploys are handled by
    // the fingerprint and need no purge — one that dropped this cache would throw away assets the
    // new build still asks for by the same URL.
    const names = await caches.keys();
    await Promise.all(names.filter(name => name !== CACHE).map(name => caches.delete(name)));

    await self.clients.claim();
})()));

self.addEventListener('fetch', (event) => {
    const request = event.request;

    if (request.method !== 'GET') return;
    if (!CACHEABLE.has(request.destination)) return;
    if (new URL(request.url).origin !== self.location.origin) return;

    event.respondWith(cacheFirst(request));
});

async function cacheFirst(request) {
    const cache = await caches.open(CACHE);

    // `ignoreVary`, because these assets are served `Vary: Accept-Encoding` to pick the gzipped
    // variant — and `Accept-Encoding` is a forbidden header name, absent from every request a
    // worker can see. Vary matching on a header that is never there is a coin toss between always
    // and never matching. The fingerprint in the URL already identifies the bytes exactly, which
    // is the only reason it is safe to ignore it.
    const hit = await cache.match(request, { ignoreVary: true });
    if (hit) return hit;

    // Not guarded: a fetch that fails here fails the request exactly as it would with no worker
    // registered, which is the behaviour every page already copes with.
    const response = await fetch(request);

    if (isImmutable(response)) {
        // Cloned because a body can only be read once and the caller is about to read this one.
        // Not awaited — the page should not wait on a write whose result it never looks at.
        cache.put(request, response.clone())
            .then(() => trim(cache))
            .catch(() => { /* a full quota is not worth failing the request over */ });
    }

    return response;
}

function isImmutable(response) {
    // 200 exactly rather than `response.ok`: `cache.put` rejects a 206, and half an asset is not
    // worth keeping. A redirected response cannot be replayed for a later request at all.
    if (response.status !== 200 || response.redirected) return false;

    return (response.headers.get('Cache-Control') || '').includes('immutable');
}

async function trim(cache) {
    const keys = await cache.keys();

    for (const key of keys.slice(0, keys.length - MAX_ENTRIES)) {
        await cache.delete(key);
    }
}
