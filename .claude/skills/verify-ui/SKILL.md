---
name: verify-ui
description: Verify a UI change across the full matrix — every affected page at mobile and desktop width, signed in as admin and as an anonymous visitor. Use after any change to a .razor file, app.css, theme.css or a scoped .razor.css, before reporting the work as done.
---

# Verifying a UI change

Every page in this app renders **four** ways: two widths × two auth states. A change verified in
one cell routinely breaks another — the season picker was hidden on mobile but not desktop, the
overflow ⋮ was misaligned only in the stacked mobile card, and the "Add Player" button is invisible
to anonymous visitors entirely. Check all four, or say which you skipped and why.

## Run the matrix first

```bash
scripts/verify-matrix.sh                       # every route without an id, on a throwaway database
scripts/verify-matrix.sh /players /settings    # the pages your change reaches
VERIFY_BASE_URL=http://localhost:5228 scripts/verify-matrix.sh /games/12/live   # your running app, for a route with an id
```

It opens each route at 1280×800 and 375×812, as a visitor and as an admin, and prints a line per
cell — where it landed, console errors, horizontal overflow — then the controls an admin sees that a
visitor does not, with the drawer's share listed once. A screenshot per cell lands in
`artifacts/verify/`. It exits non-zero on a console error, an overflow, or an admin turned away. It
needs no browser tools, so any session can run it; a route with an id needs data, which a throwaway
database has none of. **Read the admin-only list**: a control in it that a visitor should see, or one
missing from it, is the bug a screenshot would have hidden.

The steps below are for what the script cannot judge: whether a restyle looks right, and whether a
rule matches at all.

## The matrix

|  | Anonymous | Admin |
|---|---|---|
| **Desktop** (1280×800) | read-only layout, no action buttons | full action rows |
| **Mobile** (375×812) | drawer nav, stacked table cards | + icon-only header buttons |

Two columns, but authorization has more states than that:

- **Application admin** — `ApplicationAdmin` implies `Admin`, and adds `/teams` and the Teams entry
  in the drawer's Administration group. A change there needs both kinds of admin checked.
- **An admin looking at a team they do not run** — every button renders (the role claim is `Admin`),
  and every write is refused at the service. Signing in selects the account's own team to keep
  this rare; it is reachable through `/team/set`.
- **An account still on its seeded password** — it can sign in and is pinned to `/settings` until
  the password changes.

`[Authorize(Roles = AppRoles.Admin)]` is not `[Authorize]`, and `PrincipalExtensions` warns about
exactly this trap (`IsAdmin()`, never `Identity.IsAuthenticated`).

## Signing in

`GET /dev/login` signs in with no credentials — as the account named `admin`, else the first admin —
and selects that account's team. It is mapped only when the environment is Development *and* the
caller is loopback (`Web/ServiceExtensions/Routing.cs`, right after `/auth/logout`).

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
| `/players/{id}/stats` | yes | no circuit; the minutes tile, the per-game minutes column and the training attendance are admin-only |
| `/stats` | yes | no circuit; tiles, form pills, scorers. The playing-time card is admin-only; goalkeeper minutes stay public on purpose |
| `/stats/positions` | **no** — `[Authorize(Roles = Admin)]` | no circuit; position development |
| `/games` | yes | admin gets "Add" and the edit/delete icons |
| `/games/duties` | yes | no circuit; an empty duty column is left out; below 599.98px each row becomes a card |
| `/games/{id}/overview` | yes | the share/read-only view; two pitch columns collapse at 959.98px |
| `/games/{id}/result` | yes | score entry; the minutes card and the line-up per minute are admin-only |
| `/games/{id}/live` | yes, read-only | **the risky one** — per-second timer, six `AuthorizeView` blocks, its own mobile flex-`order` reflow. Admin drives the clock, goals and substitutions |
| `/games/{id}/formation` | **no** — `[Authorize(Roles = Admin)]` | drag & drop; anonymous is redirected to `/login` |
| `/trainings` | **no** — `[Authorize(Roles = Admin)]` | the training register; long-form dialog is a `.dialog-sheet` |
| `/preferences` | **no** — `[Authorize(Roles = Admin)]` | the per-season match defaults |
| `/settings` | yes | a visitor is shown the language card and the match notifications and nothing else; the season list, training schedule and password form are behind an `AuthorizeView` |
| `/users` | **no** — `[Authorize(Roles = Admin)]` | account management |
| `/teams` | **no** — `[Authorize(Roles = ApplicationAdmin)]` | clubs and teams; a team admin is refused too |
| `/styleguide` | **no** — `[Authorize(Roles = Admin)]` | no circuit; draws every design token |
| `/login`, `/not-found`, `/Error` | yes | no circuit; all localized; easy to forget when sweeping for English text |

Check the pages your change can reach, not all of them — but if you touched `app.css`,
`theme.css` or `MainLayout`, that *is* all of them.

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

1. Console clean, no horizontal overflow, and admin-only controls *absent* for a visitor — the
   matrix reports all three. A missing button is as much a bug as a broken one.
2. The expected content is present — look at the cell's screenshot, or read the page.
3. Anything you restyled: read the **computed** value, don't eyeball it.
4. If you added a class to markup, check a rule actually matches it. Scoped CSS fails **silently**:
   a class used on one page but defined in another page's `.razor.css` compiles to
   `.foo[b-otherHash]` and matches nothing, so the element renders as unstyled browser chrome.
   Anything more than one page uses belongs in `app.css`.

```js
// Does anything actually style this class?
getComputedStyle(document.querySelector('.action-btn')).width   // "32px", not "auto"
```

## Measuring instead of eyeballing

With a browser tool (the Browser pane or Claude in Chrome), measure in the page — it is more precise
than a screenshot, and it produces numbers worth quoting. Without one, `page.evaluate` in a Playwright
script runs the same lines:

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
