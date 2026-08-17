// The season picker's choice, read and written as a cookie.
//
// A cookie rather than localStorage because the choice is meant to lapse: a cookie carries its own
// expiry, so a squad that looked at last season one evening is back on the current one tomorrow
// without anything having to remember to clear it.
window.seasonCookie = {
    get() {
        const prefix = 'ff.season=';
        const hit = document.cookie.split('; ').find(c => c.startsWith(prefix));
        return hit ? decodeURIComponent(hit.substring(prefix.length)) : null;
    },

    set(value, maxAgeSeconds) {
        // Lax, because this is read by script on a page the user navigated to; there is no
        // cross-site request that needs it. Secure is left off so it still works over the plain
        // http:// of a local `dotnet run` — the value is a season id, not a credential.
        document.cookie =
            `ff.season=${encodeURIComponent(value)}; path=/; max-age=${maxAgeSeconds}; SameSite=Lax`;
    }
};
