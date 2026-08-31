# Authentication

- **`ExpireTimeSpan` does not keep anyone signed in — `IsPersistent` does.** `SignInAsync` without
  `AuthenticationProperties` sets a *session* cookie: no `Expires` on the header, so the browser is
  free to drop it whenever it decides the session ended. An eight-hour `ExpireTimeSpan` sat right
  above it and looked like the answer, but it bounds the ticket *inside* the cookie and has no say
  in whether the browser keeps the container. The symptom is phone-shaped and so reads as flaky
  rather than broken: a desktop tab holds the cookie for days, while iOS Safari and an installed PWA
  drop it every time the OS reclaims the backgrounded tab — which is a coach putting their phone
  away at half time. Both sign-in routes now pass `PersistentSession()`, and it returns a fresh
  instance per call because the cookie handler writes `IssuedUtc`/`ExpiresUtc` onto the object it is
  handed; one shared static would pin every later sign-in to the first one's expiry.
- **`SameSite=Strict` makes an ordinary link look like a logged-out session.** Strict withholds the
  cookie on *every* cross-site navigation, a plain top-level link click included — so opening the
  site from WhatsApp, an email or a search result arrives anonymous and bounces to `/login`, and
  then a reload puts it right because that navigation is same-site. Coming back on its own is what
  makes it hard to report and easy to dismiss. `Lax` is the setting; it still withholds the cookie
  on the cross-site POST that CSRF actually needs, so an *authenticated* action cannot be forged.
- **`/auth/login` is the one POST `Lax` does not cover, because it mints a cookie rather than
  needing one.** A forged cross-site POST to it would log a victim in as the attacker, so the
  endpoint validates an antiforgery token (`IAntiforgery.ValidateRequestAsync`) — the sign-in form
  renders `<AntiforgeryToken />`, and a request without a valid one is redirected to
  `/login?error=true`. It reads the form itself, so nothing validated it until asked; `/dev/login`
  is a GET and needs none. `/auth/logout` carries the token too, but there it is defence in depth —
  `Lax` already keeps the auth cookie off a cross-site POST, so a forged logout signs out nobody.
- **The login rate limiter and audit log key on `Fly-Client-IP`, not `RemoteIpAddress`.** fly-proxy
  sets that header to the real client and overwrites any the client sent, so it holds where a
  hand-rolled `X-Forwarded-For` does not — the 5-per-minute login throttle partitions on an address
  the caller cannot spoof (`ClientIp.Of`), falling back to the connection address for a local run off
  Fly.
- **Persisting data-protection keys is only half of surviving a deploy.** The keys are on the
  volume, but the purpose they are derived for defaults to the content root path — `/app` only
  because the Dockerfile says `WORKDIR /app`. Keys present on disk and derived for a different
  string open nothing, and the failure is silent: no exception, no log line, just every cookie
  rejected at once after a deploy that changed nothing about authentication.
  `SetApplicationName("FootballFormation")` is what stops it.
- **These three are browser decisions, so no C# test can see them.** All three are pinned in
  `tests/ui/specs/session.spec.js`, which reads the cookie's attributes after a real form sign-in
  and follows a link into the app from another site.
- **`OnValidatePrincipal` is not what revokes a Blazor Server session.** It runs per HTTP request,
  and a circuit makes almost none after its first page load — the rest of the session is SignalR.
  The stock `ServerAuthenticationStateProvider` reads the principal once when the circuit is created
  and never asks again, so deleting an account left the owner's open tab fully working. Measured,
  not assumed: with revalidation off, an account deleted while its owner sat idle on `/users` still
  rendered the Add User button. And this is not only a markup problem — `CircuitCurrentUser` reads
  that same provider, so `RunAdminAsync` was consulting the stale principal too.
  `RevalidatingUserAuthenticationStateProvider` closes it on a timer.
- **A rejoin does not carry stale authority through, so the retained-circuit window does not widen
  the gap.** Worth knowing before reasoning about `DisconnectedCircuitRetentionPeriod` as if it did.
  With a revoked cookie, a dropped circuit does not come back: the reconnect fails and Blazor's
  client falls back to a full page reload, which is an HTTP request, which is
  `OnValidatePrincipal` — landing on `/login`. Probed both ways round, blocking `_blazor/negotiate`
  to force the give-up path and leaving it open for a clean rejoin; both reloaded, while
  `reconnect.spec.js` shows a *valid* cookie rejoining cleanly and staying live. So the stale window
  is the revalidation interval on a connected idle circuit, and nothing more.

