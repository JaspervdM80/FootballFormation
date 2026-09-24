// The choice is kept by the host's push-shared.js, because the service worker has to read it too.
window.vibration = {
    // iOS has no Vibration API and ignores a silent notification, so it gets a pointer to its own settings instead of a switch — once
    // installed, because that is when the app first appears there.
    state: async () => {
        if (typeof navigator.vibrate !== 'function') {
            const notifications = window.matchNotifications;
            return notifications?.isIos() && notifications.isInstalled() ? 'ios' : 'unsupported';
        }
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
