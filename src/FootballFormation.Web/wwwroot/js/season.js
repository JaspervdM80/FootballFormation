// Writes the season picker's choice to a cookie. Write-only on purpose: the server reads the
// cookie off the request that renders the page (App.razor), and only setting it needs the browser,
// because a circuit that is already up has no response left to put a Set-Cookie on.
//
// A cookie rather than localStorage for both halves of that: it carries its own expiry, so the
// choice lapses on its own, and it is sent with the request, so the page can be rendered right the
// first time instead of corrected after the circuit connects.
window.seasonCookie = {
    set(value, maxAgeSeconds) {
        // Lax, because nothing cross-site needs to send this. Secure is left off so it still works
        // over the plain http:// of a local `dotnet run` — the value is a season id, not a
        // credential.
        document.cookie =
            `ff.season=${encodeURIComponent(value)}; path=/; max-age=${maxAgeSeconds}; SameSite=Lax`;
    }
};
