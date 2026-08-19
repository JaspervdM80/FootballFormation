// Copies plain text to the clipboard. Two entry points for the two ways a page can call this:
// `copyText` for an interactive page that already has the string and wants a result back through
// JS interop, and `copyElementText` for a page with no circuit — the same shape as
// captureFormationOverview in screenshot.js, reading text already rendered server-side (so the
// wording stays in the resx) and reporting success or failure by unhiding an element already on
// the page rather than composing a message here.
window.copyText = async function (text) {
    try {
        await navigator.clipboard.writeText(text);
        return true;
    } catch (error) {
        console.warn('Copy failed:', error);
        return false;
    }
};

window.copyElementText = async function (elementId, successId, errorId) {
    const element = document.getElementById(elementId);
    if (!element) return;

    const show = (id) => {
        const notice = id && document.getElementById(id);
        if (notice) notice.hidden = false;
    };

    try {
        await navigator.clipboard.writeText(element.textContent ?? '');
        show(successId);
    } catch (error) {
        console.warn('Copy failed:', error);
        show(errorId);
    }
};
