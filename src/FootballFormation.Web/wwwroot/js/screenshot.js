// Called straight from an onclick on /games/{id}/overview, which renders without a circuit. That
// makes reporting a failure this file's job too: `errorId` names an element already on the page
// carrying the message, which we unhide rather than compose anything here — the wording is the
// server's, because the resx is where a translation lives.
window.captureFormationOverview = async function (elementId, errorId) {
    const element = document.getElementById(elementId);
    if (!element) return;

    const fail = (error) => {
        console.warn('Screenshot failed:', error);
        const notice = errorId && document.getElementById(errorId);
        if (notice) notice.hidden = false;
    };

    try {
        // Loaded on demand, but from our own wwwroot rather than a CDN: this is an installable PWA,
        // and a third-party fetch at the moment of export is one that fails on a phone with no signal
        // at a pitch — which is exactly when someone shares a lineup. Still lazy, so the 194 KB is
        // only paid by the people who actually export.
        if (typeof html2canvas === 'undefined') {
            await new Promise((resolve, reject) => {
                const script = document.createElement('script');
                script.src = 'js/vendor/html2canvas.min.js';
                script.onload = resolve;
                script.onerror = reject;
                document.head.appendChild(script);
            });
        }

        // Background comes from the club theme so exported images match the app
        const themeBackground = getComputedStyle(document.documentElement)
            .getPropertyValue('--surface-appbar-alt').trim() || '#1a1a2e';

        const canvas = await html2canvas(element, {
            backgroundColor: themeBackground,
            scale: 2, // High res for WhatsApp
            useCORS: true,
            logging: false
        });

        // Convert to blob and trigger download
        canvas.toBlob(function (blob) {
            const url = URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url;
            a.download = 'formation.png';
            document.body.appendChild(a);
            a.click();
            document.body.removeChild(a);
            URL.revokeObjectURL(url);
        }, 'image/png');
    } catch (error) {
        fail(error);
    }
};
