# Push Notifications

Kick-off, every goal, and full time, to any browser that asked — no account, no sign-in. Issue
[#165](https://github.com/JaspervdM80/FootballFormation/issues/165), and the cheap experiment
[#66](https://github.com/JaspervdM80/FootballFormation/issues/66) asked for before committing to
native apps.

## The path a goal takes

```
coach taps "goal"
  → MatchGoalService.LogGoalAsync
  → LiveMatchOperation notifies with LiveMatchEvent.Goal
  → MatchNotificationSender queues it and returns immediately
  → MatchAudienceQuery reads the game, its team and that team's followers
  → MatchNotificationTextBuilder composes the words, once per culture
  → WebPush encrypts a copy per browser, signed with VAPID
  → FCM / APNs / Mozilla
  → service-worker.js `push` handler shows it
```

## Why the event carries a kind

`LiveMatchNotifier.Notify` used to carry a game id alone, which is all the live screen needs — it
reloads on any change. Push cannot work that way: a slot swap would wake every phone following the
match. `LiveMatchEvent` names the three worth waking for and leaves everything else `Other`, which is
**the default**, so a call site that says nothing can never send a notification by accident.

Widening the event rather than subscribing at three call sites keeps the guarantee
`LiveMatchOperation` exists for — the notification is part of the write shape, not a line each
service has to remember.

## The subscribe endpoint is the one anonymous write

Every other mutation goes through `ServiceOperation.RunAdminAsync`. Nobody signs in to follow a
match, so `PushSubscriptionService` uses the plain wrapper and the rate limiter stands in for the
guard — `"push"` in `Program.cs`, looser than `"login"` because one device legitimately
re-subscribes whenever the browser rotates its endpoint.

That makes it the one place an anonymous caller hands us a URL the server later POSTs to, so
`IsPushEndpoint` refuses anything that is not https, is loopback, or is not a DNS host. Both keys are
checked for being base64url of the right length before they reach the crypto.

## Endpoints are unique across every team

The push service already guarantees one endpoint per browser, so the unique index is on `Endpoint`
alone and a second subscribe is a re-subscribe. That is also why both the upsert and the delete read
`IgnoreQueryFilters()`: a browser that switched teams has to be **found past the team filter and
moved**, or the insert hits the unique index instead.

## The sender runs outside every scope

It is a `BackgroundService` and the notifier fires it from whichever circuit made the write, so:

- **It never blocks and never throws.** The handler does nothing but `TryWrite` to a bounded channel.
  A subscriber that blocks would make an admin's tap wait on FCM, and one that throws would fail the
  write itself — see `LiveMatchOperation`.
- **It has no team in scope**, because there is no request and no cookie. `MatchAudienceQuery` takes
  the raw factory, reads the game's own `TeamId` past the filters, stamps it, and lets the filters do
  the rest — the same move the boot steps in `Program.cs` make.
- **It cannot resolve a scoped service.** An earlier draft injected `PushSubscriptionService` and
  would have failed at startup; pruning lives on `MatchAudienceQuery.DropAsync` instead.

## A service worker cannot localize

There is no `IStringLocalizer` in the browser, so the server composes the finished title and body and
puts them in the payload. Each subscription records the culture it was made in, and the sender groups
by culture and swaps `CultureInfo.CurrentUICulture` around the lookup — otherwise a follower who
subscribed in English gets Dutch because the last write happened on a Dutch circuit.

## Nothing in a notification may carry playing minutes

A notification lands on a lock screen in front of whoever is holding the phone, which makes it the
least recoverable place in the app to leak what `/stats` keeps behind an admin sign-in — the same
rule the copyable match summary follows. A goal's *scoreboard* minute is public and fine; playing
time and utilisation are not. `MatchNotificationReport` carries only what the notification needs, and
a test asserts its shape so a later property cannot quietly widen it.

## The crypto is ours, and it is pinned to the spec

`Core/Push/WebPush.cs` implements RFC 8291 (`aes128gcm` payload encryption) and RFC 8292 (VAPID) over
`System.Security.Cryptography`. The one .NET package for this has been unmaintained for years and the
whole of it is about seventy lines, so there is no dependency.

**`WebPushTests` runs RFC 8291 §5 verbatim** — its keys, its salt, its expected bytes. That case is
the only thing standing between a payload that encrypts without error and one that decrypts to
nothing on the phone, which looks exactly like a working feature until nobody is woken. The
deterministic overload it needs is why `InternalsVisibleTo` exists in `FootballFormation.Core.csproj`.

## No scheduler, on purpose

Kick-off, goals and full time all happen while an admin is on the touchline with the app open, so the
machine is awake and the send is synchronous with a write. **A pre-match reminder is a different
feature** and should not be folded in here: it needs a hosted service firing against a machine that
may be suspended (`auto_stop_machines = "suspend"`, `min_machines_running = 1`).

## Configuration

`Push:PublicKey`, `Push:PrivateKey` and `Push:Subject`, from Fly secrets in production. **Absent keys
are an ordinary state** — a developer's machine and CI have none — so `PushConfiguration` reports
`IsConfigured` false, `/push/key` answers 404, and the sender logs once and does nothing. It boots
and serves either way.

The key pair is generated once and **never rotated casually**: the public half is what every browser
stored as its `applicationServerKey`, so a new one silently orphans every subscription on file.

## What web push cannot do

- **iOS needs the PWA installed first.** Safari hands out no subscription at all until the app is on
  the home screen, so `push.js` reports `install-first` and the Home row says so instead of offering
  a button that could only fail. See [known_issues/touch-pwa.md](../known_issues/touch-pwa.md).
- **Opting in is per browser, not per person.** The same parent on a phone and a laptop is two rows,
  and clearing site data unsubscribes them with no way for us to tell.
- **Endpoints go stale.** A 404 or 410 from the push service is the only signal the row is dead, and
  nothing else removes it.
- **One instance only.** `LiveMatchNotifier` is in-process, and push inherits that exactly — scaling
  out needs a real backplane, not a patch.
