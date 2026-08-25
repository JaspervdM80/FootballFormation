// Caches the assets the app is built from, and nothing else.
//
// The policy is one line: cache what the server calls immutable. MapStaticAssets fingerprints every
// asset referenced through `@Assets[...]` and serves that route `max-age=31536000, immutable`,
// while the un-fingerprinted route for the same file gets `no-cache`. So the header is the build
// stating the bytes can never change — the precondition cache-first needs.
//
// Three things fall out of reading it off the response rather than keeping a list:
//
//   * No cache version to bust. A deploy re-fingerprints what changed, so the new build asks for a
//     different URL and the old entry is never requested again.
//   * Markup is never cached, being `no-cache` — so an admin's copy of /stats and the minutes #98
//     holds back from visitors cannot be stored and served to the next person on a shared phone.
//   * Getting it wrong is a missed optimisation, never a stale asset.
//
// Not offline support: with only assets cached, a page still needs the network for its markup.
// Caching pages is #104 — a cached *interactive* page is a dead shell, and the render mode that
// decides which is which is invisible from a URL.

const CACHE = 'ff-immutable-assets';

// By destination rather than URL: it is the browser saying what the request is for, which path
// matching cannot work out. `/_blazor` negotiation is `empty` and falls out for free.
const CACHEABLE = new Set(['style', 'script', 'font', 'image']);

// A deploy orphans entries rather than replacing them, so nothing here evicts itself. Trimmed
// oldest-first; `cache.keys()` answers in insertion order.
const MAX_ENTRIES = 60;

self.addEventListener('install', () => self.skipWaiting());

self.addEventListener('activate', (event) => event.waitUntil((async () => {
    // Only caches under an older *name*, i.e. a previous shape of this file. A deploy needs no
    // purge — the fingerprint handles it, and dropping this cache would throw away assets the new
    // build still asks for by the same URL.
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

    // ignoreVary: these are served `Vary: Accept-Encoding`, and Accept-Encoding is a forbidden
    // header name absent from every request a worker sees — so varying on it is a coin toss. Safe
    // only because the fingerprint in the URL already identifies the bytes.
    const hit = await cache.match(request, { ignoreVary: true });
    if (hit) return hit;

    // Unguarded: a failed fetch fails the request exactly as it would with no worker registered.
    const response = await fetch(request);

    if (isImmutable(response)) {
        // Not awaited — the page should not wait on a write it never reads.
        cache.put(request, response.clone())
            .then(() => trim(cache))
            .catch(() => { /* a full quota is not worth failing the request over */ });
    }

    return response;
}

function isImmutable(response) {
    // 200 exactly, not `response.ok`: cache.put rejects a 206, and a redirected response cannot be
    // replayed for a later request.
    if (response.status !== 200 || response.redirected) return false;

    return (response.headers.get('Cache-Control') || '').includes('immutable');
}

async function trim(cache) {
    const keys = await cache.keys();

    for (const key of keys.slice(0, keys.length - MAX_ENTRIES)) {
        await cache.delete(key);
    }
}
