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
            return await registration.pushManager.getSubscription() ? 'on' : 'off';
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

            return await send('push/subscribe', subscription) ? 'on' : 'off';
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

    // The VAPID key travels as base64url and has to reach pushManager.subscribe as bytes.
    function decodeKey(key) {
        const padded = (key + '='.repeat((4 - key.length % 4) % 4)).replace(/-/g, '+').replace(/_/g, '/');
        const raw = atob(padded);
        return Uint8Array.from([...raw].map(c => c.charCodeAt(0)));
    }

    return { status, enable, disable };
})();
