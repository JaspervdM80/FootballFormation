// Copies text already rendered server-side (so the wording stays in the resx) to the clipboard,
// from a plain onclick — the same shape as captureFormationOverview in screenshot.js. Both the
// result page and the formation overview use this rather than a round trip through Blazor server
// interop: navigator.clipboard.writeText only runs inside the task that produced the user's own
// gesture, and by the time a click has gone click -> circuit -> JS interop and back, that gesture
// is gone — iOS Safari and Firefox refuse the call outright at that point.
window.copyElementText = async function (elementId, successId, errorId) {
    const element = document.getElementById(elementId);
    if (!element) return;

    // Only one of the two ever applies, so the other is hidden first — otherwise a failed attempt
    // followed by a successful one leaves both notices on screen at once.
    const show = (shownId, hiddenId) => {
        const shown = shownId && document.getElementById(shownId);
        const hidden = hiddenId && document.getElementById(hiddenId);
        if (hidden) hidden.hidden = true;
        if (shown) shown.hidden = false;
    };

    try {
        await navigator.clipboard.writeText(element.textContent ?? '');
        show(successId, errorId);
    } catch (error) {
        console.warn('Copy failed:', error);
        show(errorId, successId);
    }
};
