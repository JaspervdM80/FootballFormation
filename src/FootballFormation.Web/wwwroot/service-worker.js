// Caches only what the server marks `immutable`, which MapStaticAssets sets on fingerprinted
// routes alone. So there is no cache version to bust, and markup — always `no-cache` — can never be
// stored and leak an admin's /stats to the next person on a shared phone (#98). Not offline
// support; caching pages is #104.

const CACHE = 'ff-immutable-assets';

// By destination, not URL: path matching cannot tell a navigation from an asset.
const CACHEABLE = new Set(['style', 'script', 'font', 'image']);

// A deploy orphans entries rather than replacing them, so nothing evicts itself.
const MAX_ENTRIES = 60;

self.addEventListener('install', () => self.skipWaiting());

self.addEventListener('activate', (event) => event.waitUntil((async () => {
    // Older *names* only: a deploy needs no purge, and dropping this cache would discard assets
    // the new build still asks for by the same URL.
    const names = await caches.keys();
    await Promise.all(names.filter(name => name !== CACHE).map(name => caches.delete(name)));

    await self.clients.claim();
})()));

// The server composes the finished text: a worker has no access to IStringLocalizer, so nothing here is translated or even inspected
// beyond being read out of the payload.
self.addEventListener('push', (event) => {
    if (!event.data) return;

    const message = event.data.json();

    event.waitUntil(self.registration.showNotification(message.title, {
        body: message.body,
        icon: 'icons/icon-192.png',
        badge: 'icons/icon-192.png',
        // Same tag for the whole match, so a third goal replaces the second rather than stacking three rows on the lock screen.
        tag: message.tag,
        renotify: true,
        data: { url: message.url }
    }));
});

self.addEventListener('notificationclick', (event) => {
    event.notification.close();

    const url = event.notification.data && event.notification.data.url ? event.notification.data.url : '/';

    event.waitUntil((async () => {
        // Focus the match if it is already open somewhere — a parent who tapped the last goal should not get a second tab.
        const windows = await self.clients.matchAll({ type: 'window', includeUncontrolled: true });
        for (const client of windows) {
            if (new URL(client.url).pathname === url) return client.focus();
        }

        return self.clients.openWindow(url);
    })());
});

self.addEventListener('fetch', (event) => {
    const request = event.request;

    if (request.method !== 'GET') return;
    if (!CACHEABLE.has(request.destination)) return;
    if (new URL(request.url).origin !== self.location.origin) return;

    event.respondWith(cacheFirst(request));
});

async function cacheFirst(request) {
    const cache = await caches.open(CACHE);

    // ignoreVary: Accept-Encoding is a forbidden header name, absent from every request a worker
    // sees, so varying on it is a coin toss. Safe only because the fingerprint identifies the bytes.
    const hit = await cache.match(request, { ignoreVary: true });
    if (hit) return hit;

    // Unguarded: a failed fetch fails the request as it would with no worker registered.
    const response = await fetch(request);

    if (isImmutable(response)) {
        // Not awaited: the page never reads it.
        cache.put(request, response.clone())
            .then(() => trim(cache))
            .catch(() => { /* a full quota is not worth failing the request over */ });
    }

    return response;
}

function isImmutable(response) {
    // 200 exactly: cache.put rejects a 206, and a redirect cannot be replayed for a later request.
    if (response.status !== 200 || response.redirected) return false;

    return (response.headers.get('Cache-Control') || '').includes('immutable');
}

async function trim(cache) {
    const keys = await cache.keys();

    for (const key of keys.slice(0, keys.length - MAX_ENTRIES)) {
        await cache.delete(key);
    }
}
