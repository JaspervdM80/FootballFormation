// Registers the pass-through service worker that makes the app installable, and owns the install
// banner outright — the server renders it hidden and this file decides whether it is shown.
//
// It has to be this way round: every input to that decision (display-mode, the user agent, a
// dismissal in localStorage, whether Chrome ever offered a native prompt) is known only here, and
// the banner renders in the layout, which is statically rendered on every page and so has no
// circuit to ask the browser over.
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

    function shouldOffer() {
        const standalone = window.matchMedia('(display-mode: standalone)').matches
            || window.navigator.standalone === true;
        const ua = navigator.userAgent;
        // iPadOS 13+ reports as Mac, hence the touch check
        const isIos = /iPhone|iPad|iPod/.test(ua)
            || (ua.includes('Mac') && 'ontouchend' in document);
        const isMobile = isIos || /Android/.test(ua)
            || window.matchMedia('(pointer: coarse)').matches;
        const dismissed = localStorage.getItem(DISMISSED_KEY) === 'true';
        return { show: isMobile && !standalone && !dismissed, isIos };
    }

    function setUpInstallBanner() {
        const banner = document.getElementById('install-banner');
        if (!banner) return;

        const { show, isIos } = shouldOffer();
        if (!show) return;

        const instruction = banner.querySelector('[data-install-instruction]');
        const actions = banner.querySelector('.install-banner-actions');

        // iOS has no install API at all, so it gets the Share → Add to Home Screen wording and no
        // button. Everywhere else the button is worth showing even before beforeinstallprompt has
        // fired, because it can still fall back to instructions when pressed.
        if (isIos) {
            instruction.textContent = banner.dataset.instructionIos;
        } else {
            actions.hidden = false;
        }

        banner.querySelector('[data-install-dismiss]').addEventListener('click', () => {
            localStorage.setItem(DISMISSED_KEY, 'true');
            banner.hidden = true;
        });

        actions.querySelector('[data-install-prompt]')?.addEventListener('click', async () => {
            // No captured prompt event: already installed once, or a non-Chrome Android browser.
            // Fall back to telling them where the menu item is.
            if (!deferredPrompt) {
                actions.hidden = true;
                instruction.textContent = banner.dataset.instructionAndroid;
                return;
            }

            deferredPrompt.prompt();
            const choice = await deferredPrompt.userChoice;
            deferredPrompt = null;
            if (choice.outcome === 'accepted') banner.hidden = true;
        });

        banner.hidden = false;
    }

    // The drawer's open state is a checkbox in the layout, and enhanced navigation patches the DOM
    // rather than replacing it — so the element survives the navigation with `checked` still set,
    // and the drawer would stay open over the page you just moved to. Nothing else resets it: the
    // server always renders the checkbox unchecked, which is not a change the patch can see.
    function closeDrawer() {
        const toggle = document.getElementById('nav-drawer');
        if (toggle) toggle.checked = false;
    }

    function onNavigated() {
        closeDrawer();
        setUpInstallBanner();
    }

    setUpInstallBanner();

    // blazor.web.js is loaded after this file but runs before DOMContentLoaded, so window.Blazor is
    // there by now. If it somehow is not, the only cost is a drawer that stays open across a
    // navigation and a banner that does not come back — not a broken page.
    document.addEventListener('DOMContentLoaded', () => {
        window.Blazor?.addEventListener('enhancedload', onNavigated);
    });

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

