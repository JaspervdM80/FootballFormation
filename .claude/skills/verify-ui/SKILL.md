---
name: verify-ui
description: Verify a UI change across the full matrix — every affected page at mobile and desktop width, signed in as admin and as an anonymous visitor. Use after any change to a .razor file, app.css, theme.css or a scoped .razor.css, before reporting the work as done.
---

# Verifying a UI change

Every page in this app renders **four** ways: two widths × two auth states. A change verified in
one cell routinely breaks another — the season picker was hidden on mobile but not desktop, the
overflow ⋮ was misaligned only in the stacked mobile card, and the "Add Player" button is invisible
to anonymous visitors entirely. Check all four, or say which you skipped and why.

## The matrix

|  | Anonymous | Admin |
|---|---|---|
| **Desktop** (1280×800) | read-only layout, no action buttons | full action rows |
| **Mobile** (375×812) | drawer nav, stacked table cards | + icon-only header buttons |

Two columns, but authorization has **three** states. `[Authorize(Roles = AppRoles.Admin)]` is not
`[Authorize]`: a signed-in non-admin is neither of the columns above. `UserRole` has only `Admin`
today, so the third state is unreachable — but the moment a second role exists this matrix needs a
column, and `PrincipalExtensions` warns about exactly this trap (`IsAdmin()`, never
`Identity.IsAuthenticated`). An account still on its seeded password is a fourth: it can sign in
and is pinned to `/settings` until the password changes.

## Signing in

`GET /dev/login` signs in as admin with no credentials. It is mapped only when the environment is
Development *and* the caller is loopback (`Program.cs`, right after `/auth/logout`).

```
http://localhost:5228/dev/login     → admin
POST /auth/logout                   → back to anonymous
```

**Never type a password into the login form**, `admin/admin` included — that restriction is about
the action, not the value. `/dev/login` exists precisely so it never has to happen.

To get back to anonymous, submit the logout form (it is a POST) or clear the `ff.auth` cookie:

```js
document.querySelector('form[action="/auth/logout"] button').click()
```

## Routes

| Route | Anonymous | Notes |
|---|---|---|
| `/` | yes | season picker shows here but filters nothing; live-match banner on match day |
| `/players` | yes, read-only | season-scoped squad; admin gets row actions + header buttons |
| `/players/{id}/stats` | yes | |
| `/stats` | yes | season statistics: tiles, form pills, scorers, playing-time bars |
| `/games` | yes | admin gets "Add" and the edit/delete icons |
| `/games/{id}/overview` | yes | the share/read-only view; two pitch columns collapse at 959.98px |
| `/games/{id}/result` | yes | score entry |
| `/games/{id}/live` | yes, read-only | **the risky one** — per-second timer, six `AuthorizeView` blocks, its own mobile flex-`order` reflow. Admin drives the clock, goals and substitutions |
| `/games/{id}/formation` | **no** — `[Authorize(Roles = Admin)]` | drag & drop; anonymous is redirected to `/login` |
| `/settings` | **no** — `[Authorize(Roles = Admin)]` | season management and self-service password change |
| `/users` | **no** — `[Authorize(Roles = Admin)]` | account management |
| `/not-found`, `/Error` | yes | both localized; easy to forget when sweeping for English text |

Check the pages your change can reach, not all eleven — but if you touched `app.css`,
`theme.css` or `MainLayout`, that *is* all eleven.

## Breakpoints that matter

The ladder, deliberately:

- **959.98px** — MudBlazor's `md`. The formation builder stacks its three panels and the overview
  drops to one pitch column here.
- **700px** — a design tier, *not* a MudBlazor one: the inline nav goes away entirely;
  `.mud-appbar .season-picker` hides; `btn-compact` drops button labels; `.squad-actions` stacks.
  How many nav links show *above* it is not this number — the bar shows what fits and the drawer,
  which is on every width, carries the rest.
- **760px** — the two statistics pages drop from four stat tiles to two.
- **599.98px** — MudBlazor's `xs`, where it stacks a table into per-row cards. `.stacked-table`
  takes over there for the squad, users and playing-time tables. Always `599.98`, never `599` or
  `600` — `600` fires *at* the boundary MudBlazor is switching on.

Test at 375 and 1280. If a rule sits between the two, test its edge too — 700px in particular is
easy to miss from either end.

## What to check in each cell

1. `read_console_messages` — clean, ignoring SignalR reconnect noise from a restart.
2. `read_page` or `get_page_text` — the expected content is present, and admin-only controls are
   *absent* when anonymous. A missing button is as much a bug as a broken one.
3. No horizontal overflow: `document.documentElement.scrollWidth > innerWidth` must be `false`.
4. Anything you restyled: read the **computed** value, don't eyeball it.
5. If you added a class to markup, check a rule actually matches it. Scoped CSS fails **silently**:
   a class used on one page but defined in another page's `.razor.css` compiles to
   `.foo[b-otherHash]` and matches nothing, so the element renders as unstyled browser chrome.
   Anything more than one page uses belongs in `app.css`.

```js
// Does anything actually style this class?
getComputedStyle(document.querySelector('.action-btn')).width   // "32px", not "auto"
```

## Measuring instead of eyeballing

The Browser pane is often not compositing, so screenshots may be unavailable. Measure with
`javascript_tool` — it is more precise anyway, and it produces numbers worth quoting:

```js
// Vertical alignment of an icon row
const mid = el => { const r = el.getBoundingClientRect(); return r.top + r.height / 2; };
[...document.querySelectorAll('.row-actions > button')].map(mid)   // all equal, or it's misaligned

// Did a responsive rule actually apply?
getComputedStyle(document.querySelector('.squad-actions')).flexDirection
```

If a control is behind `AuthorizeView` and you cannot sign in for some reason, build a synthetic
node carrying the same class chain MudBlazor emits and read *its* computed styles. That is a
fallback, not the plan — `/dev/login` is.

## Reporting

State which of the four cells you checked and what you measured. If you skipped one, say so —
"verified on desktop, not checked on mobile" is useful; silence reads as "all fine".
