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

    // navigator.serviceWorker.ready never rejects and never times out: a worker that fails to activate leaves it pending for the life of
    // the page, and with it the interop call the Home page is awaiting. Answering null instead is what keeps that page from hanging.
    function readyRegistration() {
        return Promise.race([
            navigator.serviceWorker.ready,
            new Promise(resolve => setTimeout(() => resolve(null), 5000))
        ]);
    }

    async function status() {
        if (!supported) return 'unsupported';

        // Safari hands out no push subscription at all until the PWA is on the home screen, so asking would only ever fail.
        if (isIos() && !isInstalled()) return 'install-first';

        if (Notification.permission === 'denied') return 'blocked';

        try {
            const registration = await readyRegistration();
            if (!registration) return 'unsupported';

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

            const registration = await readyRegistration();
            if (!registration) return 'unsupported';

            const subscription = await registration.pushManager.subscribe({
                // Required by Chrome: a push that shows nothing is not allowed.
                userVisibleOnly: true,
                applicationServerKey: pushShared.decodeKey(await response.text())
            });

            if (!await send('push/subscribe', subscription)) return 'off';

            // So the worker can name this endpoint when the browser later rotates it — several browsers leave
            // pushsubscriptionchange.oldSubscription unset, and then this is the only record of which follower rotated.
            await pushShared.rememberEndpoint(subscription.endpoint);
            return 'on';
        } catch {
            return 'off';
        }
    }

    async function disable() {
        try {
            const registration = await readyRegistration();
            if (!registration) return 'off';

            const subscription = await registration.pushManager.getSubscription();
            if (!subscription) return 'off';

            // Told first, then dropped: a row for an endpoint that no longer exists would only be cleared by the next failed send.
            await send('push/unsubscribe', subscription);
            await subscription.unsubscribe();
            await pushShared.forgetEndpoint();
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

    // The toggle is handled here rather than through a Blazor OnClick, because that would arrive as a WebSocket message and
    // Notification.requestPermission() raised from one carries no user gesture — Safari refuses it outright. Delegated from the document
    // so it does not matter when the button appears, and nothing is awaited before enable() reaches the prompt.
    let page = null;

    document.addEventListener('click', event => {
        const toggle = event.target.closest?.('[data-notify-toggle]');
        if (!toggle) return;

        const answer = toggle.dataset.notifyToggle === 'on' ? disable() : enable();

        answer.then(state => page?.invokeMethodAsync('NotificationStateChanged', state));
    });

    async function bind(reference) {
        page = reference;

        const state = await status();

        // A first visit can get here while the worker is still installing, and readyRegistration gives up before it finishes. Without
        // this the row stays hidden for the life of the page and only a reload brings it back.
        if (state === 'unsupported' && supported) {
            navigator.serviceWorker.ready
                .then(status)
                .then(settled => {
                    if (settled !== 'unsupported') page?.invokeMethodAsync('NotificationStateChanged', settled);
                })
                .catch(() => { /* the worker never arrived; the row stays hidden, which is the honest answer */ });
        }

        return state;
    }

    return { status, enable, disable, bind };
})();
