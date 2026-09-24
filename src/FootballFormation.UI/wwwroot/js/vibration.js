// The choice is kept by the host's push-shared.js, because the service worker has to read it too.
window.vibration = {
    // 'unsupported' where there is nothing to drive — iOS above all — so the switch is not offered there at all.
    state: async () => {
        if (typeof navigator.vibrate !== 'function') return 'unsupported';
        return await pushShared.vibrationOn() ? 'on' : 'off';
    },

    // A short buzz on switching on, so the phone answers whether it can.
    set: async (on) => {
        await pushShared.setVibration(on);
        if (on) navigator.vibrate(200);
    },

    // Best effort by design: Chrome refuses a buzz until the page has had a tap.
    buzz: async (pattern) => {
        if (typeof navigator.vibrate === 'function' && await pushShared.vibrationOn()) navigator.vibrate(pattern);
    }
};
