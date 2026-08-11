// Starts Blazor by hand, for one reason: the retry schedule it rejoins a circuit with.
//
// The stock schedule is ten attempts with no delay between them at all, then five seconds between
// each one after that. A phone coming out of suspension has no network for the first moment, so
// all ten immediate attempts are spent and failed in a fraction of a second — and every return to
// the app therefore lands in the five-second bucket, showing "Reconnecting..." for ten seconds or
// more over a circuit the server was holding the whole time.
//
// One immediate attempt (a circuit that is genuinely still there rejoins on it, with no overlay
// worth the name), then one a second while the radio comes back, which is where a real reconnect
// lands. Falling back to five seconds after twenty attempts keeps a phone that wandered off the
// wifi from hammering the machine.
//
// The window is deliberately finite: ~70 seconds of trying, and then Blazor gives up and marks the
// dialog failed, which is what js/pwa.js watches for to reload the page. That reload is the better
// move by then — it is a plain HTTP request, so it wakes a Fly machine that has scaled to zero,
// which a WebSocket retry against a stopped machine never will.
(function () {
    // blazor.web.js is loaded with autostart="false" above this script, so nothing starts the
    // circuit if this file does not run. It not being there at all means the framework script
    // failed to load, and the page is inert either way — see known_issues.md.
    if (!window.Blazor) return;

    Blazor.start({
        circuit: {
            reconnectionOptions: {
                maxRetries: 30,
                retryIntervalMilliseconds: function (previousAttempts, maxRetries) {
                    if (maxRetries && previousAttempts >= maxRetries) return null;
                    if (previousAttempts === 0) return 0;
                    return previousAttempts < 20 ? 1000 : 5000;
                }
            }
        }
    });
})();
