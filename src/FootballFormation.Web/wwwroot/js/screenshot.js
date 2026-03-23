window.captureFormationOverview = async function (elementId) {
    const element = document.getElementById(elementId);
    if (!element) return;

    // Dynamically load html2canvas if not already loaded
    if (typeof html2canvas === 'undefined') {
        await new Promise((resolve, reject) => {
            const script = document.createElement('script');
            script.src = 'https://cdnjs.cloudflare.com/ajax/libs/html2canvas/1.4.1/html2canvas.min.js';
            script.onload = resolve;
            script.onerror = reject;
            document.head.appendChild(script);
        });
    }

    const canvas = await html2canvas(element, {
        backgroundColor: '#1a1a2e',
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
};
