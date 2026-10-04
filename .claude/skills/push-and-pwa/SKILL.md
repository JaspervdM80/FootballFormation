---
name: push-and-pwa
description: Match notifications and the installable app — LiveMatchEvent kinds, the one anonymous write, the sender that runs outside every scope, culture per subscription, no minutes in a payload, the hand-rolled Web Push crypto, and the service worker's cache rules. Use for Core/Push, Web/Push, PushSubscriptionService, MatchAudienceQuery, service-worker.js, push.js or pwa.js.
---

# Push notifications and the PWA

Kick-off, every goal and full time go to any browser that asked — no account, no sign-in.

```
touchline write → LiveMatchOperation notifies with a LiveMatchEvent
  → MatchNotificationSender queues it and returns → MatchAudienceQuery reads the game's followers
  → MatchNotificationTextBuilder, once per culture → WebPush encrypts per browser → push service
  → service-worker.js shows it
```

## Only three events wake anybody

`LiveMatchEvent` is `KickOff`, `Goal`, `FullTime` or `Other`, and **`Other` is the default**, so a
call site that says nothing can never send a notification by accident. A new event worth waking for
is a new member passed explicitly through `LiveMatchOperation` — never a second subscriber at a call
site, which is the line each service would have to remember.

## The one anonymous write

`PushSubscriptionService` uses `RunAsync`, not `RunAdminAsync`: nobody signs in to follow a match.
The `"push"` rate limiter stands in for the guard, and the service validates every field itself —
it is the one place an anonymous caller hands the server a URL it later POSTs to.

- `IsPushEndpoint` refuses anything not https, loopback, or not a DNS host, and **redirects are off**
  on the `WebPush` client (`AllowAutoRedirect = false`), or a `307` walks straight past that check.
- The public key is **imported, not just measured**: a key of the right length can still throw
  inside `ECDiffieHellman`, and one planted row would take the whole match's fan-out down.
- Endpoints are unique across every team, so subscribe, renew and unsubscribe read past the team
  filter with `IgnoreQueryFilters()` — a browser that switched teams is found and moved.
- A renewal (`/push/renew`, from the worker's `pushsubscriptionchange`) copies team and culture off
  the **old row**, never the request: the worker's cookies are not a choice the follower made.

## The sender runs outside every scope

`MatchNotificationSender` is a `BackgroundService` fired from whichever circuit made the write:

- **It never blocks and never throws.** The handler only `TryWrite`s to a bounded channel; a slow
  handler would make the coach's tap wait on FCM, a throwing one would fail the write.
- **It has no team in scope** — no request, no cookie. `MatchAudienceQuery` takes the raw factory,
  reads the game's own `TeamId` and stamps it.
- **It cannot take a scoped service.** Pruning lives on `MatchAudienceQuery.DropAsync` for that reason.
- **One instance only.** `LiveMatchNotifier` is in-process, and push inherits it.

## What a payload may carry

- **No playing minutes, ever.** A notification lands on a lock screen in front of whoever holds the
  phone. A goal's scoreboard minute is fine; playing time and utilisation are not.
  `MatchNotificationReportTests` pins the report's shape so a later property cannot widen it.
- **Finished text, in the follower's language.** A service worker has no `IStringLocalizer`, so the
  server composes title and body. Each subscription records its culture and the sender groups by it,
  or a follower who subscribed in English gets Dutch off whichever circuit made the write.

## The crypto is ours

`Core/Push/WebPush.cs` implements RFC 8291 and RFC 8292 over `System.Security.Cryptography`, with no
package. **`WebPushTests` runs RFC 8291 §5 verbatim** — the only thing between a payload that
encrypts without error and one that decrypts to nothing on the phone. Which push-service answers
prune a row lives in `PushDeliveryOutcome`, beside its tests, because `Web` has none: only 404 and
410 mean gone, and treating a 429 that way would unsubscribe a browser that was only throttled.

`Push:PublicKey`/`PrivateKey`/`Subject` come from Fly secrets. Absent keys are an ordinary state —
dev and CI have none, and the app boots and serves. **Never rotate the key pair casually**: every
browser stored the public half, so a new one silently orphans every subscription on file.

## The service worker

- It caches **only what the server marks `immutable`** — fingerprinted assets, by request
  destination, never markup. Markup cached on a shared phone would leak an admin's `/stats` to the
  next person (#98). There is no cache version to bump. `service-worker.spec.js` pins it.
- `activate` deletes every cache but the asset one **and `ff-push`**, which holds the subscribed
  endpoint (several browsers leave `event.oldSubscription` unset) and the vibration choice. Purging
  it loses which follower a rotated subscription belonged to.
- `showNotification` throws on `silent: true` with `renotify: true`, and the notification is lost.
- `pwa.js` owns the install banner outright: everything that decides it is known only in the
  browser, and the layout it renders in has no circuit. iOS hands out no subscription until the app
  is installed, which `push.js` reports as `install-first`.

Detail: [docs/patterns/](../../../docs/patterns/push-notifications.md) ·
[docs/known_issues/](../../../docs/known_issues/touch-pwa.md)
