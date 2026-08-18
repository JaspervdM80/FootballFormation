// Minimal service worker for installability.
//
// Pass-through: every request goes straight to the network and nothing is cached. The fetch
// handler still has to exist for Android/Chrome to treat the app as installable.
//
// That used to be the only sane choice — every interaction ran over the SignalR circuit, so a
// cached page would have been a dead shell. **It is no longer true of the whole app.** The
// statistics pages, the player pages and the match report are plain server HTML now, and a cached
// copy of one is a page that still works. Caching those, and only those, is the obvious follow-up:
// they are also the pages someone opens on a phone with no signal at a pitch. What has not changed
// is that caching an interactive page is still a dead shell, so any cache added here has to know
// which is which — the render mode is the answer, and it is not visible from the URL.
self.addEventListener('install', () => self.skipWaiting());
self.addEventListener('activate', (event) => event.waitUntil(self.clients.claim()));
self.addEventListener('fetch', () => { /* network only — no interception */ });
