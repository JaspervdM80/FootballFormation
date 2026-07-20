// Minimal service worker for installability.
//
// This is a Blazor Server app: every meaningful interaction runs over the SignalR
// circuit, so offline caching of pages would only produce dead shells. We therefore
// pass every request straight through to the network and keep no cache. The fetch
// handler still has to exist for Android/Chrome to treat the app as installable.
self.addEventListener('install', () => self.skipWaiting());
self.addEventListener('activate', (event) => event.waitUntil(self.clients.claim()));
self.addEventListener('fetch', () => { /* network only — no interception */ });
