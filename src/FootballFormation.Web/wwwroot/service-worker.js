// Minimal service worker for installability.
//
// Pass-through: every request goes straight to the network and nothing is cached. The fetch
// handler still has to exist for Android/Chrome to treat the app as installable.
//
// That used to be the only sane choice, because every interaction ran over the SignalR circuit.
// Since the render-mode split it is only half true: a cached copy of a statically rendered page
// still works, while a cached interactive page is as dead a shell as ever. Any cache added here
// therefore has to tell the two apart, and the render mode is not visible from the URL.
self.addEventListener('install', () => self.skipWaiting());
self.addEventListener('activate', (event) => event.waitUntil(self.clients.claim()));
self.addEventListener('fetch', () => { /* network only — no interception */ });
