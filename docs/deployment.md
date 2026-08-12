# Deployment — Fly.io + gjs-meiden.nl

The app is deployed to [Fly.io](https://fly.io) (region `ams`, Amsterdam) as a Docker container
with the custom domain **gjs-meiden.nl**. Chosen because Blazor Server needs WebSockets and
SQLite needs a persistent disk, and Fly provides both plus free TLS certificates for
custom domains at the lowest price point (~$3–5/month, less with scale-to-zero).

## Moving parts

| File | Purpose |
|------|---------|
| `Dockerfile` | Multi-stage build (SDK → aspnet runtime), listens on 8080 |
| `global.json` | Pins the SDK for CI and web containers. `.dockerignore` keeps it out of the image, which builds on `sdk:10.0` — see [known_issues.md](known_issues.md) |
| `fly.toml` | App `gjs-meiden`, volume `data` mounted at `/data` with 30-day snapshot retention, scale-to-zero enabled |
| `Program.cs` | `APP_DATA_DIR` env var overrides the data folder (DB, logs, data-protection keys); maps `/health` |

On Fly, `APP_DATA_DIR=/data` points at a 1 GB persistent volume, so the SQLite DB,
Serilog logs, and data-protection keys all survive deploys and restarts.
`ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` (set in the Dockerfile) makes the app trust
Fly's `X-Forwarded-Proto` header — without it `UseHttpsRedirection` would loop, because
Fly terminates TLS at the edge and forwards plain HTTP to port 8080.

## One-time setup

```powershell
# 1. Install the CLI
winget install --id flyctl

# 2. Create an account (needs a credit card) or log in
fly auth signup    # or: fly auth login

# 3. From the repo root: create the app, volume, and IPs
fly apps create gjs-meiden
fly volumes create data --region ams --size 1 --app gjs-meiden
fly ips allocate-v4 --shared --app gjs-meiden   # free shared IPv4
fly ips allocate-v6 --app gjs-meiden

# 4. First deploy (remote builder — local Docker not required)
fly deploy

# 5. Attach the domain
fly certs add gjs-meiden.nl --app gjs-meiden
fly certs add www.gjs-meiden.nl --app gjs-meiden
```

## DNS records (at the domain's DNS provider)

`fly ips list --app gjs-meiden` shows the actual addresses; add:

| Type | Host | Value |
|------|------|-------|
| A | `@` | the shared IPv4 from `fly ips list` |
| AAAA | `@` | the IPv6 from `fly ips list` |
| CNAME | `www` | `gjs-meiden.fly.dev` |

`fly certs check gjs-meiden.nl` reports when validation and the Let's Encrypt
certificate are done (usually minutes after DNS propagates).

## Redeploying after changes

Merging to `main` **is** the release. GitHub Actions picks up the merge commit
(`.github/workflows/fly-deploy.yml`, authenticated by the `FLY_API_TOKEN` secret — a scoped deploy
token from `flyctl tokens create deploy --app gjs-meiden`) and the deploy job starts straight away:
no gate job in front of it, no approval to click, nothing at *Waiting*.

That is affordable because the weight sits at the other end. Every check on the pull request has to
be green and the branch has to be up to date with `main` before the merge button unlocks at all, so
the decision to merge and the decision to release are the same decision, taken once. See
[Only a green build can be merged](#only-a-green-build-can-be-merged) below.

Pull requests are gated by a **separate** workflow, `.github/workflows/ci.yml` ("CI"), which runs
four jobs: `🔨 Build and test` (restore, a Release build where warnings are errors, and the test
suite), `📊 Coverage`, `🎭 Playwright` and `📸 Visual check`. The emoji are part of each job's name
and therefore part of the check's context — see the renaming note below. It holds no deploy job and
no Fly token, so a pull request cannot reach the volume even in principle; the deploy workflow does
not trigger on `pull_request` at all. Nothing re-runs on `main` afterwards — those four checks are
the last word on the commit that reaches the volume. What compiles it again is the Docker build
inside `flyctl deploy`, so a merge that does not build fails the deploy rather than shipping; what proves
the result is live is the smoke check. Deploying also requires `main`, which stops a
`workflow_dispatch` run against a feature branch from putting that branch into production.

## Only a green build can be merged

A workflow can report a failure but it cannot refuse a merge — that is a repository setting, and
until it is switched on a red PR merges as easily as a green one. `.github/rulesets/main-every-check-green.json`
is that setting, written down: a GitHub **ruleset** covering the default branch which

- requires a pull request, so nothing lands on `main` by direct push;
- requires **all four** checks — `🔨 Build and test`, `📊 Coverage`, `🎭 Playwright` and
  `📸 Visual check` — so the merge button stays disabled while any of them is queued, running, or
  failing;
- requires the branch to be **up to date with `main`** before it can land;
- blocks deletion and force-pushes on `main`, since the deploy history is what a rollback reads;
- grants **no bypass to anyone**, including the repo owner.

Four rather than one because merging now deploys. Nothing runs between the merge button and the Fly
volume, so a check that only reports is a check nobody is obliged to read, and the browser jobs are
the only thing in this repository that knows whether a page renders at all.

**GitHub does not read this file from the repository.** Nothing in a repo can grant itself branch
protection — that would rather defeat the point. The file is an importable export, and it has to be
applied once by hand:

> *Settings → Rules → Rulesets* → delete the ruleset already there → **New ruleset → Import a
> ruleset** → upload `.github/rulesets/main-every-check-green.json` → **Create**.

Import always *creates*, so deleting the old one first is what stops two rulesets applying at once.
Check it took by opening any pull request: the merge button should be greyed with *Required statuses
must pass before merging*, and name all four. After that the file is the record of what is
configured — change the rule here and re-import, so the setting is reviewable in a diff like
everything else.

**All four have to run on every pull request, or the guard inverts.** A required check that never
reports leaves a PR pending forever rather than mergeable, so `ci.yml` deliberately carries no
`paths:` filter — adding one later to "skip CI for docs" would silently wedge every docs-only PR,
and there are now four checks to wedge it rather than one.

**Renaming a job is renaming a required check**, and that includes putting an emoji in front of it:
a context is matched as one whole string, so `Coverage` and `📊 Coverage` are two different checks
and the first one stops reporting the moment the job is renamed. Three things move together — the
job's `name:` in `ci.yml`, the `context` in the ruleset file, and the ruleset **in the repository's
settings**, which only changes when the file is re-imported. Re-import while the renaming pull
request is open, or that pull request waits forever on the old names it no longer reports: it is the
one change whose own merge depends on the setting it edits.

`📊 Coverage` is safe to require for the same reason the filter is not: a change with no coverable
line in it — docs, CSS, a workflow — measures nothing and passes, rather than dividing by zero and
going red. It is the 80% floor on *changed* lines, so a pull request that touches no code has
nothing to fall short of.

`ci.yml` triggers on `pull_request` and nothing else automatic — it also carried a `push` trigger
until every pull request was found to be building twice (see `testing.md`, "What triggers it"). A
pull request showing no checks at all, rather than a red one, is what a regression here looks like:
re-run it from *Actions → CI → Run workflow*, or push one more commit.

**That escape hatch is `workflow_dispatch`, and it reports the same four contexts.** Check runs
attach to a commit, so a dispatched run against a branch posts its results onto the pull request
whose head that commit is, and can unlock the merge button on its own. What it built is not quite
the same thing: a dispatch checks out the **branch tip**, where the `pull_request` event resolves to
`refs/pull/N/merge` — this branch already merged into `main`. Being up to date with `main` is
required, so the two are usually the same tree; on a wedged pull request, prefer one more commit and
keep the dispatch for when there is nothing to push.

**A flake now blocks a merge.** That is the cost of requiring the browser jobs, and it is the
intended one: re-run the failed job from the run's page. There is no bypass, so a red check that
will not go green has to be fixed or the ruleset has to be disabled, which is visible.

**A restricted actions policy stops the check before it can report — and it looks like nothing ran.**
Under *Settings → Actions → General → Actions permissions*, the owner-only option ("Allow
JaspervdM80 actions and reusable workflows") blocks every third-party action. Both workflows here
use them, so GitHub refuses each run about a second after creating it, with:

> The actions `actions/checkout@v7` and `actions/setup-dotnet@v6` are not allowed in
> `JaspervdM80/FootballFormation` because all actions must be from a repository owned by
> `JaspervdM80`.

That conclusion is `startup_failure`: **no job is created, so no check is created either**, and a
pull request shows no checks at all rather than a red one. It reads like CI never triggered. It
did — it was refused. The run is also not re-runnable (`This workflow run cannot be retried`), so
recovering needs a fresh push, or closing and reopening the pull request.

If the policy must stay restricted, the allow-list needs **both** of:

| Entry | Covers |
|-------|--------|
| *Allow actions created by GitHub* (checkbox) | `actions/checkout`, `actions/setup-dotnet` — `ci.yml` |
| `superfly/*` | `superfly/flyctl-actions/setup-flyctl` — `fly-deploy.yml` |

**The second is the one that bites quietly.** `ci.yml` uses no third-party action, so ticking only
the GitHub box turns every check green while `fly-deploy.yml` still fails at startup — and since a
refused run creates no job, the merge that was meant to release produces nothing to look at. The
symptom is not a red build but a site that silently stops updating.
Whenever this policy is touched, check the deploy workflow too, not just the pull request.

**Deliberately enabled: *require branches to be up to date*** (`strict_required_status_checks_policy`).
It forces a pull request whose base has moved to take `main` on board and go green again before it
can land — the *Update branch* button, then four checks over from the top.

It was off for a long time, on the grounds that the case it protects against — a PR that passed
against a stale `main` and breaks once merged — was caught before it could reach production, because
`fly-deploy.yml` re-ran CI on `main` and the deploy depended on it. That re-run is gone: merging
deploys, and nothing tests the merged result any more. This policy is what replaced it, and the two
are a pair — turning it off means putting the gate job back.

`actions/checkout` resolving a `pull_request` event to `refs/pull/N/merge` covers part of the same
ground already, since the checks are built on this branch *already merged into* `main`. What it
cannot cover is `main` moving after those checks went green, which is exactly the window this
closes.

The cost is real and worth naming: every *Update branch* click re-runs all four checks, browsers
included, so a merge behind a moved `main` is about twenty minutes rather than four.

**The escape hatch is the ruleset, not a bypass.** With `bypass_actors` empty there is no way to
merge past a red build quietly; an emergency means setting the ruleset to *Disabled*, which is a
visible, logged act that shows up in the repo's rule insights. That is the intended trade — the
guard is worth little if the person most likely to be in a hurry can step around it silently.

It costs more than it used to. While the gate job existed, disabling the ruleset only skipped the
*merge* check and the commit was still built and tested on `main` before Fly saw it. There is no
such second look now: disabling the ruleset means an unbuilt, untested commit merges, deploys, and
migrates the live volume, in that order and within a couple of minutes. Turn it back on afterwards.

**`required_approving_review_count` is 0, and one account is the reason.** GitHub refuses to let
anyone approve their own pull request — the *Approve* radio is not rendered on your own PR, and the
API answers `422 Can not approve your own pull request`. There is no setting that relaxes it; every
neighbouring knob (`require_last_push_approval`, an environment's *Prevent self-review*) only
tightens it. This repository has exactly one collaborator, and **the pull requests an assistant
opens are authored by that same account** — Claude Code pushes with the owner's token, so #46, #48
and #57 all read `JaspervdM80` however they were written. So a required approval here has only two
settings, and neither is a gate: raise the count and *every* pull request becomes unmergeable,
including the assistant's; grant *Repository admin* a bypass to fix that and every pull request is
exempt again. GitHub cannot tell a maintainer's own work from an agent acting as them, because
there is nothing in the request that differs.

What is enabled instead is **`required_review_thread_resolution`**, which does bite with one
account: it needs no approval, only that every review comment thread is resolved before the merge
button unlocks. Leave a note on a diff and the merge waits for you to deal with it.

**Adding a second account as a collaborator is the only way to make the merge itself require another
pair of eyes**; if that happens, set the count to 1 and leave `bypass_actors` empty. Until then the
four checks are what a merge has to satisfy, and the person clicking merge is the review.

## Only one person can deploy

Three things could put a release on the volume, and each is closed separately. A required reviewer on
the `production` environment used to stand across the first two of them; removing it is what made
merging the release. That decision did not disappear, it moved to the merge button, which needs four
green checks and a branch up to date with `main`, and which only somebody with write access can
press.

**A push to `main`.** Impossible directly — the ruleset above requires a pull request and grants no
bypass. What lands on `main` lands by merge, and a merge now deploys within a minute or two of the
click, with nothing to approve on the way. The `production` environment still exists and the job
still names it, but only for its secret and its branch policy; its *Required reviewers* box is
empty, and re-ticking it is how to put the pause back for a risky release.

**A `workflow_dispatch` run.** Anyone with write access can start one. The job's `if` refuses a
dispatch from anybody but `github.repository_owner`, refuses any ref that is not `main`, and refuses
one whose `confirm` input is not the word `deploy`. With both the gate job and the reviewer gone,
those three are the whole of what stands in front of a manual release rather than a tidy-up in front
of one — the input in particular is there because a stray *Run workflow* click would otherwise
replace the container that is serving, which mid-match drops the live screen's circuits. The `push`
arm of the same condition tests the ref and nothing else, so a merge still deploys with nothing to
fill in.

**Reading the token out of a workflow on a branch.** This is the one a branch rule does not cover,
and the one the removed approval never covered either. A repository secret is readable by any
workflow in the repository, so a `.github/workflows/*.yml` added on a feature branch could have
printed `FLY_API_TOKEN` and deployed from a laptop afterwards — no merge involved.
`FLY_API_TOKEN` is therefore an **environment** secret on
`production`, not a repository secret, and `production` sets its deployment branch policy to `main`
only. A job that does not name the environment cannot see the secret; a job that does name it is
refused on any other ref, before it starts. Keeping a copy at repository level would reopen the
hole silently, so there must not be one.

Setting that up is manual — nothing in a repository can grant itself these, for the same reason a
repository cannot grant itself branch protection:

> *Settings → Environments → New environment → `production`* → leave **Required reviewers**
> unticked → under **Deployment branches and tags** choose *Selected branches* and add `main` →
> **Save**. Then *Environment secrets → Add secret* → `FLY_API_TOKEN`, and **delete the
> repository-level secret of the same name** under *Settings → Secrets and variables → Actions*.

The reviewer box is the only part of that which is a preference rather than a rule. Ticking it and
adding yourself is how a deploy goes back to holding at *Waiting* until somebody clicks *Approve* —
worth doing for a release you want to sit on, and worth unticking again afterwards, because a run
left waiting parks every later one behind it (see the concurrency note below).

**Create the environment before this workflow reaches `main`.** A job naming an environment that
does not exist does not fail — GitHub creates it silently, with no branch policy — and repository
secrets are still visible to it. The branch policy is the whole point of it now, so the difference
between the environment being there and being conjured is not something the run would show you.

**Deploys queue, they do not overlap.** `concurrency: fly-deploy` has `cancel-in-progress: false`,
so two merges close together release in order rather than racing — the right trade for a single
volume that migrates on boot. Nothing waits for a person any more, so the queue drains on its own;
it only stops draining if the reviewer box has been ticked and a run is sitting at *Waiting*, and
then it is "approve or cancel" rather than "ignore".

**Outside GitHub, the token and the Fly org are the boundary.** The workflow's token is scoped with
`flyctl tokens create deploy --app gjs-meiden`, so it can deploy this app and nothing else. Anyone
in the Fly organisation can still `fly deploy` from their own machine, and no GitHub setting reaches
that — audit both directly:

```powershell
fly orgs show <org>      # who else can deploy this app at all
fly tokens list          # revoke anything unrecognised with: fly tokens revoke <id>
```

Manual deploys from the repo root still work, and deliberately skip everything above — the checks,
the merge, and the smoke check:

```powershell
fly deploy
```

Either way, migrations run automatically on startup, same as locally.

## The repository is public, and what a stranger can actually do

`gjs-meiden` is a **public** repository with forking enabled, no forks, one collaborator
(`JaspervdM80`, admin) and no outside collaborators. Anyone on GitHub can read it, fork it, open an
issue, and open a pull request. None of that is a way in, and it is worth being precise about why,
because the honest answer is not "the ruleset stops them".

**Merging needs write access, and nobody else has any.** A stranger's pull request is a request; the
merge button is only rendered for a collaborator. The ruleset on `main` is the second line, not the
first — it means even an account *with* write access cannot push to `main` directly or merge past a
red check, because `bypass_actors` is empty.

**Nobody joins a repository; they are let in.** Since a merge is now a release, it is worth being
exact about the ways another account could ever come to press that button. There are five, and every
one of them starts with an action taken here:

| Route | What it takes | Could they merge? |
|---|---|---|
| A collaborator invite | *Settings → Collaborators → Add people*, and they accept | Yes, at Write and above |
| A GitHub App | Installing one that asks for *Contents: write* / *Pull requests: write* | Yes, its token merges as the repository |
| A deploy key | Adding one with **Allow write access** ticked | It can push branches; `main` still needs a pull request |
| An Actions token | A workflow declaring `contents: write`, added by somebody who can push a branch | Only a pull request that already satisfies every rule — the ruleset binds `GITHUB_TOKEN` too |
| The owner's account | A stolen token, or no second factor | Yes, as the owner |

Nothing a stranger can do unilaterally appears in that table, which is the point. What it also shows
is where the guard stops: **the ruleset covers `main` only**, so an invited collaborator could push
feature branches freely and open as many pull requests as they liked — they simply could not land
one past four red checks.

The Actions row is the subtle one, and worth reading twice. *Workflow permissions: read-only* in the
repository settings is a **default, not a ceiling** — a workflow that declares
`permissions: contents: write` gets it regardless, and a workflow added on a feature branch runs
from that branch without being merged first (which is the same property that makes an environment
secret, rather than a repository secret, the thing protecting `FLY_API_TOKEN` above). What stops
that workflow merging anything it likes is not the setting, it is the ruleset: `GITHUB_TOKEN` is
bound by required checks and by `bypass_actors` being empty, exactly like a person. The setting
still matters for the workflow that forgets to say anything, which is most of them.

**The lever not pulled, on purpose.** A *second* ruleset restricting **updates** to `main`, with
bypass granted only to the *Repository admin* role, would mean a Write collaborator, an installed
app and an Actions token could none of them merge — only an admin. It is not here because with one
admin and no other collaborator it guards nothing that is not already shut, and a bypass list is a
thing that rots quietly. Add it on the day somebody is invited, as its own ruleset: bypass is granted
per ruleset, so putting the admin role on the existing one would exempt admins from the four checks
as well.

**Deploying needs the token, and a fork cannot see it.** GitHub withholds every secret from a
workflow triggered by a fork's pull request and issues it a read-only `GITHUB_TOKEN`. Since
`FLY_API_TOKEN` moved onto the `production` environment, it is not a repository secret at all any
more, and the environment admits `main` only.

**What is genuinely open is the runner.** `ci.yml` triggers on `pull_request`, so a fork's pull
request runs `dotnet restore`, `dotnet build` and `dotnet test` **on our runner, from their
branch** — a build target, a test, or a `nuget.config` in that branch is arbitrary code execution
by definition. With no secrets and a read-only token the blast radius is a throwaway container and
the Actions minutes it burns, not the repository and not the volume. GitHub's default for a public
repository, *Require approval for first-time contributors*, stops that only the first time; a
stranger who has landed one trivial pull request runs unattended after that. Close it:

> *Settings → Actions → General → Fork pull request workflows from outside collaborators* →
> **Require approval for all outside collaborators**.

**That now covers the browser jobs too, which it did not use to.** They lived in a `ui-checks.yml`
that triggered on `push`, and a push to a fork runs in the fork's own Actions rather than here.
Folding them into `ci.yml` put them on `pull_request` with everything else, so a fork's branch also
gets `npm install`, a Chromium download and a browser driving its code on our runner. That is more
arbitrary code execution in the same throwaway container with the same read-only token — the
setting above is what bounds it, and it matters more than it did.

`ci.yml` does not use `pull_request_target`, which is the trigger that *does* hand a fork's branch a
writable token, and it should not.

**Forking cannot be switched off on a public repository** — the setting exists only for private
repositories in an organisation. Making this repository private is the only thing that removes fork
pull requests and drive-by issues outright, and it is a product decision rather than a security one.

**The realistic route to a stranger merging is the table above, not the fork.** None of those five
is something a branch rule touches. Worth confirming periodically:

| Where | What to check |
|---|---|
| *Settings → Collaborators* | Only `JaspervdM80`, and no pending invitations |
| *Settings → Integrations → GitHub Apps* | Every installed app, and whether it needs write |
| *Settings → Deploy keys* | Empty, or nothing with **Allow write access** |
| *Settings → Actions → General* | Workflow permissions **read-only**, and *Allow GitHub Actions to create and approve pull requests* **off** |
| Account settings | Two-factor authentication or a passkey on the owner account |

Both workflows here declare `permissions: contents: read` at the top, so the token they get is
read-only regardless of what the repository default is set to. The repository default still matters
for anything added later that forgets to.

## A deploy has to prove it serves

`flyctl deploy` reporting success only means the machine started. After it, the workflow requests
`https://gjs-meiden.nl/health` and fails the job unless it answers `200` **and reports the commit
this run built**. The public hostname is used on purpose, so DNS and the certificate are part of
what gets checked rather than only the container.

```jsonc
{
  "status": "healthy",
  "version": "d5ba72bb10ada2aa04ef454a7c4a15c5de691da3",  // the commit this image was built from
  "appliedMigrations": 17,
  "pendingMigrations": 0,
  "detail": null                                           // why, when unhealthy
}
```

**Why the commit is in there.** A 200 answers a weaker question than it appears to: it says *a*
container is up, not that the one just built is the one answering. Fly can report a successful
deploy while the previous machine carries on serving, and nothing about that looks wrong — the site
is up, it is simply the old site, and the change you shipped is quietly missing. The commit comes
from the `GIT_SHA` build arg (`Dockerfile` → `APP_GIT_SHA`), which `fly-deploy.yml` sets to
`github.sha`; the smoke step compares the two and only passes when they match. Built outside CI it
reads `unknown`.

**Why the migration counts are in there.** The app migrates itself on boot, so anything still
pending by the time it serves means the boot did not finish its job — and a half-applied schema is
the worst kind of running: the pages that touch untouched tables work, and the rest fail strangely.
`pendingMigrations > 0` is reported unhealthy for that reason (`HealthReport`, pinned by
`HealthReportTests`). The check also runs a real query rather than `CanConnectAsync`, which for
SQLite only opens the file and so succeeds against exactly the damaged schema worth catching.

The request is retried five times with a growing pause (5 s, 10 s, 15 s, 20 s), because the machine
is usually cold-starting from zero. A healthy response carrying the *previous* commit is treated as
a retry rather than a failure — that is a deploy mid-swap, not a broken one.

**Deliberately a one-shot request, not a `[[http_service.checks]]` block in `fly.toml`.** Fly's
proxy health checks count towards the concurrency its autostop decision reads, so a check running
every few seconds holds the machine awake and quietly undoes scale-to-zero — the thing that keeps
this app at a few euros a month. A check that has to be paid for continuously to tell us something
we only need to know at deploy time is the wrong trade here.

**Deliberately not wired to an automatic rollback.** Reverting the *image* on a failed smoke check
looks like the obvious next step, and for code alone it would be — `fly deploy --image` is cheap and
lossless. It is unsafe here because by the time the smoke check runs, the release has already
migrated the database, and several migrations are one-way in practice (`AddMatchTypeAndComments`
drops a column). Rolling the image back would leave the previous code running against the new
schema, which is a second, worse failure on top of the first, and an unattended one. A failed smoke
check is a loud red deploy that a person then decides about — see *Rolling back is the image, not
the database* below.

Manual deploys skip the smoke step, so check it by hand after one:

```powershell
curl https://gjs-meiden.nl/health
```

## The database is snapshotted before every migration

Startup does three things in order, in `Program.cs` (see `Core/Data/DatabaseSafety.cs`):

1. **If — and only if — migrations are pending**, copy the database to
   `/data/backups/pre-migration-<last applied migration>.db`, keeping the newest 5. The copy is
   taken with SQLite's own backup API rather than a file copy, because with WAL journalling the
   `.db` file on its own can be missing the most recent writes. It is written under a `.tmp` name
   and moved into place, so a container killed midway never leaves a truncated file under the name
   that means "this state is safely backed up".
2. Apply the migrations.
3. `PRAGMA integrity_check` and `PRAGMA foreign_key_check`. Either failing throws, so a damaged
   database stops the boot loudly instead of serving wrong answers.

**One snapshot per schema state, not per attempt.** The name is the migration the database is
sitting on, so a state is backed up once however many times the app tries to migrate away from it.
That is what makes a crash loop survivable: several migrations here run outside a transaction, so
one that fails partway leaves the rest pending — and Fly restarts the machine. Named for the
moment instead, every restart wrote a fresh snapshot of the *broken* database and pruned an older
one, so five restarts destroyed the only good copy in about as many minutes.

**A refused boot exits non-zero.** The startup `catch` logs `Fatal` and sets `Environment.ExitCode`
(`Program.cs`). Without it the process ended successfully and the refusal never left the container:
Fly saw a clean exit and the deploy that caused it reported success while the site was down. Every
guard above is written to stop the boot loudly, and the exit code is the only part anyone outside
the log can hear — followed now by the smoke check above, which fails the deploy from the outside
even in the case where the process somehow stays up without serving.

**A failed backup aborts the migration.** That is deliberate: several migrations are one-way in
practice (`AddMatchTypeAndComments` drops a column, `AddMustChangePasswordAndLineupUniqueIndex`
deletes rows), so once one has run without a snapshot there is no route back. A container that
refuses to start is an afternoon's problem; a season of lineups quietly rewritten is permanent.
If the app will not boot for this reason, the volume is full — that is the thing to fix.

To recover from a bad migration: stop the machine, replace `/data/footballformation.db` with the
newest `pre-migration-*.db`, and deploy the previous image.

```powershell
fly ssh console -C "ls -la /data/backups"                      # what snapshots exist
fly ssh sftp get /data/backups/pre-migration-<migration>.db    # pull one down
```

Snapshots are pre-migration only, and they live on the volume they protect — they cover a bad
migration, which is what they were built for, but not a lost or corrupted volume. For that, and
for the ordinary match-day damage no migration was involved in, there is a second layer.

## Fly's volume snapshots cover what the pre-migration copies cannot

`fly.toml` sets `snapshot_retention = 30` on the `data` mount. Fly snapshots the volume daily and
keeps those copies **off** the volume, which is the half of the backup story `/data` cannot tell:
the pre-migration copies sit on the disk they protect, so a lost volume takes the database and
every snapshot of it in one go.

Thirty days rather than Fly's five-day default, and rather than nothing at all in version control.
The point is a season's data surviving a problem nobody noticed over a school holiday — a squad
edited wrongly in October is not usually discovered the same week. Sixty is the maximum Fly allows.

```powershell
fly volumes list --app gjs-meiden                      # the volume id (vol_...)
fly volumes snapshots list <volume-id>                 # what exists, and how old
fly volumes snapshots create <volume-id>               # take one now, before anything risky

# Restore: a NEW volume built from a snapshot. It must be named `data` — that is the
# `source` fly.toml mounts at /data — and cannot be smaller than the snapshot it came from.
fly volumes create data --snapshot-id <snapshot-id> --region ams --size 1 --app gjs-meiden
```

Restoring never overwrites: Fly builds a *new* volume from the snapshot, and the machine is then
moved onto it. The damaged volume stays until it is destroyed, so it can still be read from — and
because a single machine must attach exactly one volume named `data` (see the cost note at the
bottom), the old one has to go before the app is scaled back up. That is deliberate friction, and
it is why a restore is an act someone performs rather than something a deploy could do on its own.

The two layers answer different questions and neither replaces the other: the pre-migration copy is
the only thing precise enough to undo a schema change, and the Fly snapshot is the only thing that
survives losing the volume.

## Still open

- **Both backup layers live in the same Fly account.** The snapshots are off-volume, not off-Fly:
  an account-level loss, or a `fly apps destroy` typed at the wrong app, still takes everything.
  A copy pulled down periodically (`fly ssh sftp get /data/footballformation.db`) and kept
  elsewhere is what would close that. A one-click database export from inside the app would be the
  same gap answered from the other side.
- **Rolling back is the image, not the database.** `fly releases` and `fly deploy --image` undo the
  code cheaply and losslessly. Restoring the database stays manual and deliberate on purpose: live
  match mode writes continuously on match day, so an automatic restore would silently discard the
  substitutions and goals logged pitchside since the bad release booted — the same loss the
  snapshots exist to prevent. The exception is a migration that failed before the app ever served,
  which is exactly the case the per-schema-state snapshot preserves.

## Useful commands

```powershell
fly logs                 # live server logs (Serilog console output)
fly status               # machine state (stopped = scaled to zero, normal)
fly ssh console          # shell inside the container
fly ssh sftp get /data/footballformation.db backup.db   # DB backup
curl https://gjs-meiden.nl/health                       # does it serve? ("healthy")
```

## Cost control

- `auto_stop_machines = "stop"` + `min_machines_running = 0`: the VM stops when no one
  is connected and auto-starts on the next request (~2–5 s cold start). A team app used
  a few hours a week mostly costs storage (~$0.15/GB/month) plus pennies of runtime.
- Single 512 MB shared-CPU machine ≈ $3–4/month if it ran 24/7 — treat that as the ceiling.
- **Do not** scale to more than one machine: SQLite lives on one volume; a second machine
  would get its own empty volume and a split-brain database.
- Where scale-to-zero shows up in the app, so nobody hunts it as a bug: a stopped machine has no
  circuits, so a phone returning to a live match after the machine went down cannot rejoin. It
  reloads, and pays the cold start plus the page's own load on top. Nothing is lost — the match
  clock is anchored in the database, not in the circuit — and
  `DisconnectedCircuitRetentionPeriod` (see `Program.cs`) covers the ordinary case, a machine that
  is still up.
