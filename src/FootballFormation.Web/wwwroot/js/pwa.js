// Registers the pass-through service worker that makes the app installable,
// and exposes window.pwaInstall for the InstallBanner component.
(function () {
    if ('serviceWorker' in navigator) {
        navigator.serviceWorker.register('service-worker.js').catch(err =>
            console.warn('Service worker registration failed:', err));
    }

    // Chrome/Edge on Android fire this when the app is installable; capturing it
    // suppresses the browser's own mini-infobar so our banner's button can trigger it.
    let deferredPrompt = null;
    window.addEventListener('beforeinstallprompt', e => {
        e.preventDefault();
        deferredPrompt = e;
    });
    window.addEventListener('appinstalled', () => { deferredPrompt = null; });

    const DISMISSED_KEY = 'pwa-install-dismissed';

    window.pwaInstall = {
        getStatus() {
            const standalone = window.matchMedia('(display-mode: standalone)').matches
                || window.navigator.standalone === true;
            const ua = navigator.userAgent;
            // iPadOS 13+ reports as Mac, hence the touch check
            const isIos = /iPhone|iPad|iPod/.test(ua)
                || (ua.includes('Mac') && 'ontouchend' in document);
            const isMobile = isIos || /Android/.test(ua)
                || window.matchMedia('(pointer: coarse)').matches;
            return {
                installed: standalone,
                isIos: isIos,
                isMobile: isMobile,
                dismissed: localStorage.getItem(DISMISSED_KEY) === 'true'
            };
        },
        // Returns 'accepted', 'dismissed', or 'unavailable' (no captured prompt event)
        async prompt() {
            if (!deferredPrompt) return 'unavailable';
            deferredPrompt.prompt();
            const choice = await deferredPrompt.userChoice;
            deferredPrompt = null;
            return choice.outcome;
        },
        dismiss() { localStorage.setItem(DISMISSED_KEY, 'true'); }
    };

    // Phones suspend a backgrounded PWA, which kills the SignalR circuit. Blazor then
    // gives up and leaves a dead page behind, so reload as soon as that happens (and on
    // return to the app) to land back on a live, correctly styled page.
    const FAILED = ['components-reconnect-failed', 'components-reconnect-rejected'];
    const modal = document.getElementById('components-reconnect-modal');
    const RELOAD_STAMP_KEY = 'pwa-last-auto-reload';
    const RELOAD_MIN_INTERVAL_MS = 10000;

    function reloadIfDead() {
        if (!modal || !FAILED.some(c => modal.classList.contains(c))) return;

        // Guard against a reload loop when the page serves but the circuit never
        // connects (blocked WebSocket, dead network): leave the overlay up instead.
        const last = Number(sessionStorage.getItem(RELOAD_STAMP_KEY)) || 0;
        if (Date.now() - last < RELOAD_MIN_INTERVAL_MS) return;

        sessionStorage.setItem(RELOAD_STAMP_KEY, String(Date.now()));
        window.location.reload();
    }

    if (modal) {
        new MutationObserver(reloadIfDead).observe(modal, {
            attributes: true,
            attributeFilter: ['class']
        });
    }

    document.addEventListener('visibilitychange', () => {
        if (document.visibilityState === 'visible') reloadIfDead();
    });
})();

