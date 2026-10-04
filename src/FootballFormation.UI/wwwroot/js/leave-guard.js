// Loaded ahead of back.js: a capturing listener on window runs in the order it was added, and this one has to be able to stop that one.
// Native confirm() rather than a MudBlazor dialog, because only a synchronous answer can hold a click before enhanced navigation acts on it.
(() => {
    let released = null;

    // Once the circuit has failed the edits are gone already, and pwa.js's reload is the way back.
    const circuitLost = () => {
        const modal = document.getElementById('components-reconnect-modal');
        return !!modal && ['components-reconnect-failed', 'components-reconnect-rejected'].some(c => modal.classList.contains(c));
    };

    const guard = () => {
        const marker = document.querySelector('[data-leave-guard]');
        return marker && marker !== released && !circuitLost() ? marker : null;
    };

    function staysPut() {
        const marker = guard();
        if (!marker) return false;
        if (!confirm(marker.dataset.leaveGuard)) return true;

        released = marker;
        return false;
    }

    window.addEventListener('click', (event) => {
        if (event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;

        const link = event.target.closest?.('a[href]');
        if (!link || link.target === '_blank' || link.hasAttribute('download')) return;

        const url = new URL(link.href, location.href);
        if (url.origin !== location.origin || (url.pathname === location.pathname && url.search === location.search)) return;

        if (staysPut()) {
            event.preventDefault();
            event.stopImmediatePropagation();
        }
    }, true);

    window.addEventListener('beforeunload', (event) => {
        if (!guard()) return;
        event.preventDefault();
        event.returnValue = '';
    });

    // The browser's own back button and gesture. Only some browsers let a traversal be cancelled; elsewhere this does nothing.
    if ('navigation' in window) {
        navigation.addEventListener('navigate', (event) => {
            if (event.navigationType === 'traverse' && event.cancelable && staysPut()) event.preventDefault();
        });
    }
})();
