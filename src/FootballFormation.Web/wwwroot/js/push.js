// Everything the browser half of match notifications needs, as one object the Home page calls over JS interop. It reports a state
// rather than a boolean because "not now" has four different answers, and only one of them is worth showing a button for.
window.matchNotifications = (function () {
    const supported = 'serviceWorker' in navigator && 'PushManager' in window && 'Notification' in window;

    function isIos() {
        const ua = navigator.userAgent;
        // iPadOS 13+ reports as Mac, hence the touch check — same test as the install banner in pwa.js.
        return /iPhone|iPad|iPod/.test(ua) || (ua.includes('Mac') && 'ontouchend' in document);
    }

    function isInstalled() {
        return window.matchMedia('(display-mode: standalone)').matches || window.navigator.standalone === true;
    }

    async function status() {
        if (!supported) return 'unsupported';

        // Safari hands out no push subscription at all until the PWA is on the home screen, so asking would only ever fail.
        if (isIos() && !isInstalled()) return 'install-first';

        if (Notification.permission === 'denied') return 'blocked';

        try {
            const registration = await navigator.serviceWorker.ready;
            const subscription = await registration.pushManager.getSubscription();
            if (!subscription) return 'off';

            // The server decides, not this browser's copy: a row pruned after a 410 would otherwise leave the toggle reading "on" while
            // nothing is ever delivered again.
            const response = await fetch('push/known', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ endpoint: subscription.endpoint })
            });

            if (!response.ok) return 'on';

            return (await response.json()).known ? 'on' : 'off';
        } catch {
            return 'unsupported';
        }
    }

    async function enable() {
        if (!supported) return 'unsupported';

        // Must be reached from the click that called this, or Safari and Firefox refuse the prompt outright.
        if (await Notification.requestPermission() !== 'granted') return 'blocked';

        try {
            const response = await fetch('push/key');
            if (!response.ok) return 'unsupported';

            const registration = await navigator.serviceWorker.ready;
            const subscription = await registration.pushManager.subscribe({
                // Required by Chrome: a push that shows nothing is not allowed.
                userVisibleOnly: true,
                applicationServerKey: decodeKey(await response.text())
            });

            if (!await send('push/subscribe', subscription)) return 'off';

            // So the worker can name this endpoint when the browser later rotates it — several browsers leave
            // pushsubscriptionchange.oldSubscription unset, and then this is the only record of which follower rotated.
            await remember(subscription.endpoint);
            return 'on';
        } catch {
            return 'off';
        }
    }

    async function disable() {
        try {
            const registration = await navigator.serviceWorker.ready;
            const subscription = await registration.pushManager.getSubscription();
            if (!subscription) return 'off';

            // Told first, then dropped: a row for an endpoint that no longer exists would only be cleared by the next failed send.
            await send('push/unsubscribe', subscription);
            await subscription.unsubscribe();
            await forget();
        } catch {
            // Nothing to report — the state is read back either way.
        }

        return 'off';
    }

    async function send(url, subscription) {
        const json = subscription.toJSON();

        const response = await fetch(url, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                endpoint: json.endpoint,
                p256dh: json.keys.p256dh,
                auth: json.keys.auth
            })
        });

        return response.ok;
    }

    // Written here and read by the service worker, which shares this origin's caches — the worker has no other way to learn the endpoint
    // it is replacing. Keep the names in step with ENDPOINT_CACHE / ENDPOINT_KEY in service-worker.js.
    async function remember(endpoint) {
        try {
            const cache = await caches.open('ff-push');
            await cache.put('/push/last-endpoint', new Response(endpoint));
        } catch {
            // A browser refusing the Cache API only loses the silent-renewal path; the toggle still reconciles on the next launch.
        }
    }

    async function forget() {
        try {
            await (await caches.open('ff-push')).delete('/push/last-endpoint');
        } catch {
            // As above.
        }
    }

    // The VAPID key travels as base64url and has to reach pushManager.subscribe as bytes.
    function decodeKey(key) {
        const padded = (key + '='.repeat((4 - key.length % 4) % 4)).replace(/-/g, '+').replace(/_/g, '/');
        const raw = atob(padded);
        return Uint8Array.from([...raw].map(c => c.charCodeAt(0)));
    }

    return { status, enable, disable };
})();
