// Caches the assets the app is built from, and nothing else.
//
// Cache what the server calls immutable: MapStaticAssets serves a fingerprinted route
// `max-age=31536000, immutable` and the un-fingerprinted route for the same file `no-cache`, so the
// header is the build stating the bytes can never change.
//
// Reading that off the response rather than keeping a list means there is no cache version to bust,
// and markup — always `no-cache` — can never be stored, so an admin's /stats and the minutes #98
// holds back cannot reach the next person on a shared phone.
//
// Not offline support: a page still needs the network for its markup. Caching pages is #104.

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
