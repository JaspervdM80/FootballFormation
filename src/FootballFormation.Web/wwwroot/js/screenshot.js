// Called straight from an onclick on /games/{id}/overview, which renders without a circuit. That
// makes reporting a failure this file's job too: `errorId` names an element already on the page
// carrying the message, which we unhide rather than compose anything here — the wording is the
// server's, because the resx is where a translation lives.

// html2canvas 1.4.1 predates color-mix(): Chrome resolves it to color(srgb r g b / a), and the parser
// throws on a colour function it does not know — so every mixed shade is flattened before it looks.
const SRGB_COLOR = /color\(srgb\s+([-\d.eE]+)\s+([-\d.eE]+)\s+([-\d.eE]+)(?:\s*\/\s*([-\d.eE]+))?\)/g;

// What html2canvas reads a colour out of. Anything else it never parses, so flattening it is noise.
const COLOR_PROPERTIES = [
    'color', 'background-color', 'background-image', 'border-top-color', 'border-right-color',
    'border-bottom-color', 'border-left-color', 'box-shadow', 'text-shadow',
    'text-decoration-color', '-webkit-text-stroke-color'
];

function flattenColor(value) {
    return value.replace(SRGB_COLOR, (_, red, green, blue, alpha) =>
        `rgba(${Math.round(red * 255)}, ${Math.round(green * 255)}, ${Math.round(blue * 255)}, ${alpha === undefined ? 1 : alpha})`);
}

// In html2canvas's clone rather than on the page, which leaves the page alone and is the only side a
// ::before can be reached from: by then it is a real node carrying the pseudo-element's own styles.
function flattenModernColors(root, view) {
    for (const element of [root, ...root.querySelectorAll('*')]) {
        const computed = view.getComputedStyle(element);

        for (const property of COLOR_PROPERTIES) {
            const value = computed.getPropertyValue(property);
            if (value.includes('color(srgb')) {
                element.style.setProperty(property, flattenColor(value), 'important');
            }
        }
    }
}

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
            logging: false,
            onclone: (clonedDocument, clone) => flattenModernColors(clone, clonedDocument.defaultView)
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
