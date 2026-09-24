// The back arrow's href is only its fallback: where this tab actually came from is something only the browser knows.
(() => {
    if (!('navigation' in window)) return;

    const storageKey = (url) => `ff.page:${new URL(url, location.origin).pathname}`;

    // By the path the page on screen says it is, not by the current entry: enhanced navigation moves to the next entry before it has
    // fetched it, so for a moment the two disagree. sessionStorage because that is per tab and survives a reload.
    function rememberPageOnScreen() {
        const page = document.querySelector('[data-page-path]');
        if (!page) return;

        const key = storageKey(page.dataset.pagePath);
        if (page.dataset.pageName) sessionStorage.setItem(key, page.dataset.pageName);
        else sessionStorage.removeItem(key);
    }

    // The nearest earlier entry this app can name and that is not this page again — /games → /login → /games returns past both.
    function previousPage() {
        const current = navigation.currentEntry;
        const entries = navigation.entries();
        for (let i = current.index - 1; i >= 0; i--) {
            const name = sessionStorage.getItem(storageKey(entries[i].url));
            if (name && entries[i].url !== current.url) return { entry: entries[i], name };
        }
        return null;
    }

    // Lazily, on the way to showing the tooltip, because an island's first render replaces the arrow the prerender drew.
    function relabel(event) {
        const button = event.target.closest?.('a.back-button');
        const previous = button && previousPage();
        if (!previous) return;

        const label = button.dataset.backFormat.replace('{0}', previous.name);
        button.title = label;
        button.setAttribute('aria-label', label);
    }

    // Also on the way out, since an enhanced navigation replaces the page without running this script again. An address-bar
    // navigation raises no navigate event, only a pagehide.
    rememberPageOnScreen();
    navigation.addEventListener('navigate', rememberPageOnScreen);
    window.addEventListener('pagehide', rememberPageOnScreen);

    document.addEventListener('pointerover', relabel);
    document.addEventListener('focusin', relabel);

    // Capturing on window, ahead of the listener Blazor's enhanced navigation keeps on the document. A modified click still opens the
    // fallback, in whatever the modifier asked for.
    window.addEventListener('click', (event) => {
        if (event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;

        const previous = event.target.closest?.('a.back-button') && previousPage();
        if (!previous) return;

        event.preventDefault();
        event.stopPropagation();
        navigation.traverseTo(previous.entry.key);
    }, true);
})();
