(() => {
    let wanted = false;
    let lock = null;
    let asking = null;

    const acquire = async () => {
        if (!wanted || lock || asking || document.visibilityState !== 'visible' || !('wakeLock' in navigator)) return;

        try {
            asking = navigator.wakeLock.request('screen');
            const granted = await asking;
            granted.addEventListener('release', () => { if (lock === granted) lock = null; });
            // Switched off while the browser was still answering.
            if (wanted) lock = granted;
            else await granted.release();
        } catch {
            // Refused by a battery saver or a policy: the match runs the same, the phone just locks on its own schedule.
        } finally {
            asking = null;
        }
    };

    // The browser drops the lock whenever the page is hidden, so every return to it has to ask again.
    document.addEventListener('visibilitychange', acquire);

    window.liveMatch = {
        // <PageTitle> sets only the first title: HeadOutlet renders statically, so a change made on the circuit never reaches <head>.
        setTitle: (title) => {
            document.title = title;
        },

        keepAwake: async (on) => {
            wanted = on;
            if (on) {
                await acquire();
                return;
            }

            const held = lock;
            lock = null;
            await held?.release();
        }
    };
})();
