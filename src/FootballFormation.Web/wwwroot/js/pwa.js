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
})();

