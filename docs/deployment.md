# Deployment — Fly.io + gjs-meiden.nl

The app is deployed to [Fly.io](https://fly.io) (region `ams`) as a Docker container with the custom
domain **gjs-meiden.nl**. Blazor Server needs WebSockets and SQLite needs a persistent disk; Fly
provides both plus free TLS at the lowest price point.

One-time account, volume, IP, certificate and DNS setup is done and not repeated here — `fly apps
create` / `fly volumes create` / `fly certs add`, and A/AAAA/CNAME records at the registrar.

## Moving parts

| File | Purpose |
|------|---------|
| `Dockerfile` | Multi-stage build (SDK → aspnet runtime), listens on 8080 |
| `global.json` | Pins the SDK for CI and web containers. `.dockerignore` keeps it out of the image, which builds on `sdk:10.0` — see [known_issues.md](known_issues.md) |
| `fly.toml` | App `gjs-meiden`, volume `data` mounted at `/data` with 30-day snapshot retention, scale-to-zero enabled |
| `Program.cs` | `APP_DATA_DIR` env var overrides the data folder (DB, logs, data-protection keys); maps `/health` |

`APP_DATA_DIR=/data` points at a 1 GB persistent volume, so the SQLite DB, Serilog logs and
data-protection keys survive deploys. Surviving on disk is only half of what a key ring needs:
`AddDataProtection().SetApplicationName("FootballFormation")` pins the purpose the keys are derived
for, which otherwise defaults to the content root path and would change with the Dockerfile's
`WORKDIR`. Both halves have to hold or a deploy signs everyone out with nothing in the log to say
why.

`ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` (set in the Dockerfile) makes the app trust Fly's
`X-Forwarded-Proto` — without it `UseHttpsRedirection` loops, because Fly terminates TLS at the edge
and forwards plain HTTP to 8080.

## Redeploying after changes

Merging to `main` **is** the release. `fly-deploy.yml` picks up the merge commit (authenticated by
the `FLY_API_TOKEN` secret) and starts straight away: no gate job, no approval, nothing at *Waiting*.

That is affordable because the weight sits at the other end — every check has to be green and the
branch up to date with `main` before the merge button unlocks, so merging and releasing are the same
decision. See [Only a green build can be merged](#only-a-green-build-can-be-merged).

Pull requests are gated by a **separate** workflow, `ci.yml`, running four jobs: `Build and test`
(restore, a Release build where warnings are errors, the suite), `Coverage`, `Playwright` and
`Visual check`. It holds no deploy job and no Fly token, so a pull request cannot reach the volume
even in principle. Nothing re-runs on `main` afterwards — those four checks are the last word on the
commit that reaches the volume.

## Only a green build can be merged

A workflow can report a failure but cannot refuse a merge — that is a repository setting.
`.github/rulesets/main-every-check-green.json` is that setting written down: a ruleset on the default
branch which requires a pull request, requires **all four** checks, requires the branch to be **up to
date with `main`**, blocks deletion and force-pushes, and grants **no bypass to anyone**.

It is applied by hand from the GitHub UI — nothing in a repo can grant itself branch protection. The
file is the reviewable record of what is configured; change it and re-import.

**All four have to run on every pull request, or the guard inverts.** A required check that never
reports leaves a PR pending forever rather than mergeable, so `ci.yml` deliberately carries no
`paths:` filter — adding one to "skip CI for docs" would silently wedge every docs-only PR. If a job
is renamed, the ruleset's `context` must be renamed in the same change.

`Coverage` is safe to require for the same reason: the floor is on *changed* lines, so a change with
no coverable line in it measures nothing and passes rather than dividing by zero and going red.

`ci.yml` triggers on `pull_request` and nothing else automatic — a `push` trigger was removed after
every pull request was found to be building twice (see [testing.md](testing.md)). A pull request
showing *no* checks rather than a red one is what a regression here looks like. The escape hatch is
`workflow_dispatch`, which reports the same four contexts but checks out the **branch tip** where
`pull_request` resolves to `refs/pull/N/merge`; prefer one more commit and keep the dispatch for when
there is nothing to push.

**A flake now blocks a merge.** That is the intended cost of requiring the browser jobs: re-run the
failed job. There is no bypass, so an emergency means setting the ruleset to *Disabled* — a visible,
logged act — after which an unbuilt, untested commit merges, deploys and migrates the live volume
within a couple of minutes. Turn it back on.

`required_approving_review_count` is 0 because GitHub refuses to let anyone approve their own pull
request and this repository has one collaborator — whom an assistant's pull requests are also
authored by. What is enabled instead is **`required_review_thread_resolution`**, which does bite with
one account: every review thread must be resolved before the merge button unlocks.

## Only one person can deploy

**`FLY_API_TOKEN` is an *environment* secret on `production`, not a repository secret**, and
`production` restricts deployment branches to `main`. This is load-bearing: a repository secret is
readable by any workflow in the repository, so a `.github/workflows/*.yml` added on a feature branch
could print it and deploy from a laptop afterwards, no merge involved. A job that does not name the
environment cannot see the secret; one that does is refused on any other ref. **There must not be a
repository-level copy.**

The deploy job's `if` refuses a dispatch from anybody but `github.repository_owner`, refuses any ref
that is not `main`, and refuses one whose `confirm` input is not the word `deploy` — a stray *Run
workflow* click would otherwise replace the serving container, which mid-match drops the live
screen's circuits.

`concurrency: fly-deploy` has `cancel-in-progress: false`, so two merges close together release in
order rather than racing — the right trade for a single volume that migrates on boot.

`ci.yml` does **not** use `pull_request_target`, the trigger that hands a fork's branch a writable
token, and it must not. A fork's pull request runs our build and tests on our runner with no secrets
and a read-only token; the blast radius is a throwaway container.

Manual `fly deploy` from the repo root still works and deliberately skips everything above — the
checks, the merge and the smoke check. Migrations run on startup either way.

**The live volume is a schema ahead of the repository's history.** `Migrations/` holds one file,
`20260322100416_InitialCreate`, into which the twenty that built the schema were folded — and it
keeps that original id precisely because `/data/footballformation.db` already lists it in
`__EFMigrationsHistory`. Production therefore boots with nothing pending and never applies it, which
is the whole point: a new id would have re-run `CREATE TABLE` against a live database. That volume's
history still names the nineteen that followed; EF ignores rows it has no file for. Anything
scaffolded from here is an ordinary migration on top — see
[patterns.md](patterns.md#migrations-are-one-file) before rescaffolding the first one.

## A deploy has to prove it serves

`flyctl deploy` reporting success only means the machine started. After it, the workflow requests
`https://gjs-meiden.nl/health` and fails the job unless it answers `200` **and reports the commit
this run built**. The public hostname is used on purpose, so DNS and the certificate are part of what
gets checked.

```jsonc
{
  "status": "healthy",
  "version": "d5ba72bb10ada2aa04ef454a7c4a15c5de691da3",  // the commit this image was built from
  "appliedMigrations": 20,
  "pendingMigrations": 0,
  "detail": null                                           // why, when unhealthy
}
```

**Why the commit is in there.** A 200 says *a* container is up, not that the one just built is
answering. Fly can report a successful deploy while the previous machine carries on serving, and
nothing about that looks wrong — the site is up, it is simply the old site. The commit comes from the
`GIT_SHA` build arg, which `fly-deploy.yml` sets to `github.sha`; the smoke step only passes when the
two match. Built outside CI it reads `unknown`.

**Why the migration counts are in there.** The app migrates itself on boot, so anything still pending
by the time it serves means the boot did not finish — and a half-applied schema is the worst kind of
running: pages touching untouched tables work, the rest fail strangely. `pendingMigrations > 0` is
reported unhealthy for that reason (`HealthReport`, pinned by `HealthReportTests`). The check runs a
real query rather than `CanConnectAsync`, which for SQLite only opens the file and so succeeds
against exactly the damaged schema worth catching.

The request is retried five times with a growing pause, because the machine is usually cold-starting.
A healthy response carrying the *previous* commit is a retry, not a failure — that is a deploy
mid-swap.

**Deliberately a one-shot request, not a `[[http_service.checks]]` block in `fly.toml`.** Fly's proxy
health checks count towards the concurrency its autostop decision reads, so a check running every few
seconds holds the machine awake and quietly undoes scale-to-zero.

**Deliberately not wired to an automatic rollback.** By the time the smoke check runs, the release has
already migrated the database, and a migration is one-way in practice as soon as it drops a column or
deletes rows — several of the ones this schema was built from did. Rolling the image back would leave
the previous code running against the new schema — a second, worse, unattended failure. A failed
smoke check is a loud red deploy that a person then decides about.

## The database is snapshotted before every migration

Startup does three things in order, in `Program.cs` (see `Core/Data/DatabaseSafety.cs`):

1. **If — and only if — migrations are pending**, copy the database to
   `/data/backups/pre-migration-<last applied migration>.db`, keeping the newest 5. The copy uses
   SQLite's own backup API rather than a file copy, because with WAL journalling the `.db` file alone
   can be missing the most recent writes. It is written under a `.tmp` name and moved into place, so
   a container killed midway never leaves a truncated file under the name that means "safely backed
   up".
2. Apply the migrations.
3. `PRAGMA integrity_check` and `PRAGMA foreign_key_check`. Either failing throws, so a damaged
   database stops the boot loudly instead of serving wrong answers.

**One snapshot per schema state, not per attempt.** The name is the migration the database is sitting
on, so a state is backed up once however many times the app tries to migrate away from it. That is
what makes a crash loop survivable: several migrations run outside a transaction, so one failing
partway leaves the rest pending — and Fly restarts the machine. Named for the moment instead, every
restart wrote a fresh snapshot of the *broken* database and pruned an older one, so five restarts
destroyed the only good copy in about as many minutes.

**A refused boot exits non-zero.** The startup `catch` logs `Fatal` and sets `Environment.ExitCode`.
Without it the process ended successfully and the refusal never left the container: Fly saw a clean
exit and the deploy reported success while the site was down.

**A failed backup aborts the migration.** A migration that drops a column or deletes rows is one-way
in practice, and this schema was built from several that did, so once one has run without a snapshot
there is no route back. A container that refuses to start is an afternoon's
problem; a season of lineups quietly rewritten is permanent. If the app will not boot for this
reason, the volume is full.

To recover: stop the machine, replace `/data/footballformation.db` with the newest
`pre-migration-*.db`, and deploy the previous image.

## Fly's volume snapshots cover what the pre-migration copies cannot

`fly.toml` sets `snapshot_retention = 30` on the `data` mount. Fly snapshots the volume daily and
keeps those copies **off** the volume — the pre-migration copies sit on the disk they protect, so a
lost volume takes the database and every snapshot of it in one go. Thirty days rather than Fly's
five-day default, because a squad edited wrongly in October is not usually discovered the same week.

Restoring builds a *new* volume from a snapshot (`fly volumes create data --snapshot-id …`); it must
be named `data` and cannot be smaller than the snapshot. The damaged volume stays until destroyed, so
it can still be read from — and a single machine must attach exactly one volume named `data`, so the
old one has to go before scaling back up. That friction is why a restore is an act someone performs
rather than something a deploy could do.

The two layers answer different questions: the pre-migration copy is the only thing precise enough to
undo a schema change, the Fly snapshot the only thing that survives losing the volume.

## Still open

- **Both backup layers live in the same Fly account.** The snapshots are off-volume, not off-Fly: an
  account-level loss still takes everything.
- **Rolling back is the image, not the database.** `fly releases` and `fly deploy --image` undo the
  code cheaply. Restoring the database stays manual on purpose: live match mode writes continuously
  on match day, so an automatic restore would silently discard the substitutions and goals logged
  pitchside since the bad release booted. The exception is a migration that failed before the app
  ever served, which is what the per-schema-state snapshot preserves.

## Useful commands

```powershell
fly logs                 # live server logs (Serilog console output)
fly status               # machine state (stopped = scaled to zero, normal)
fly ssh console          # shell inside the container
fly ssh sftp get /data/footballformation.db backup.db   # DB backup
curl https://gjs-meiden.nl/health                       # does it serve? ("healthy")
```

## Cost control

- `auto_stop_machines = "stop"` + `min_machines_running = 0`: the VM stops when no one is connected
  and auto-starts on the next request (~2–5 s cold start).
- **Do not** scale to more than one machine: SQLite lives on one volume; a second machine would get
  its own empty volume and a split-brain database.
- Where scale-to-zero shows up in the app, so nobody hunts it as a bug: a stopped machine has no
  circuits, so a phone returning to a live match after the machine went down cannot rejoin. It
  reloads, and pays the cold start plus the page's own load. Nothing is lost — the match clock is
  anchored in the database, not in the circuit — and `DisconnectedCircuitRetentionPeriod` (see
  `Program.cs`) covers the ordinary case, a machine that is still up.
