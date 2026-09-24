// Loaded by the page and imported by the service worker, because both halves of a subscription renewal need the same three things and a
// rename in one file alone is a follower nobody can reach again. `self` is the global in both.
self.pushShared = (function () {
    // Not an asset cache: where the replaced endpoint and the vibration choice are kept, the only storage both the page and the worker
    // can read. Exempted by name from the purge in service-worker.js's activate, which otherwise deletes every cache but the asset one.
    const CACHE = 'ff-push';
    const KEY = '/push/last-endpoint';
    const VIBRATION_KEY = '/push/vibration';

    // The VAPID key travels as base64url and has to reach pushManager.subscribe as bytes.
    function decodeKey(key) {
        const padded = (key + '='.repeat((4 - key.length % 4) % 4)).replace(/-/g, '+').replace(/_/g, '/');
        const raw = atob(padded);
        return Uint8Array.from([...raw].map(c => c.charCodeAt(0)));
    }

    async function rememberEndpoint(endpoint) {
        try {
            await (await caches.open(CACHE)).put(KEY, new Response(endpoint));
        } catch {
            // A browser refusing the Cache API only loses the silent-renewal path; the toggle still reconciles on the next launch.
        }
    }

    async function rememberedEndpoint() {
        try {
            const hit = await (await caches.open(CACHE)).match(KEY);
            return hit ? await hit.text() : null;
        } catch {
            return null;
        }
    }

    async function forgetEndpoint() {
        try {
            await (await caches.open(CACHE)).delete(KEY);
        } catch {
            // As above.
        }
    }

    // On unless switched off, so a browser that cannot keep the choice still buzzes.
    async function vibrationOn() {
        try {
            const hit = await (await caches.open(CACHE)).match(VIBRATION_KEY);
            return !hit || await hit.text() !== 'off';
        } catch {
            return true;
        }
    }

    async function setVibration(on) {
        await (await caches.open(CACHE)).put(VIBRATION_KEY, new Response(on ? 'on' : 'off'));
    }

    return { CACHE, decodeKey, rememberEndpoint, rememberedEndpoint, forgetEndpoint, vibrationOn, setVibration };
})();
