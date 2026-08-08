# Deployment — Fly.io + gjs-meiden.nl

The app is deployed to [Fly.io](https://fly.io) (region `ams`, Amsterdam) as a Docker container
with the custom domain **gjs-meiden.nl**. Chosen because Blazor Server needs WebSockets and
SQLite needs a persistent disk, and Fly provides both plus free TLS certificates for
custom domains at the lowest price point (~$3–5/month, less with scale-to-zero).

## Moving parts

| File | Purpose |
|------|---------|
| `Dockerfile` | Multi-stage build (SDK → aspnet runtime), listens on 8080 |
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

Merging to `main` deploys automatically via GitHub Actions
(`.github/workflows/fly-deploy.yml`, authenticated by the `FLY_API_TOKEN` repo secret —
a scoped deploy token from `flyctl tokens create deploy --app gjs-meiden`).

Pull requests are gated by a **separate** workflow, `.github/workflows/ci.yml` ("CI"), which runs
`Build and test` — restore, a Release build (where warnings are errors) and the test suite. It
holds no deploy job and no Fly token, so a pull request cannot reach the volume even in principle;
the deploy workflow no longer triggers on `pull_request` at all. `fly-deploy.yml` calls the same
CI workflow as its gate (`uses: ./.github/workflows/ci.yml`), so what runs before a deploy is
literally what ran on the PR rather than a copy that can drift. Deploying additionally requires
`main`, which stops a `workflow_dispatch` run against a feature branch from putting that branch
into production.

## Only a green build can be merged

A workflow can report a failure but it cannot refuse a merge — that is a repository setting, and
until it is switched on a red PR merges as easily as a green one. `.github/rulesets/main-build-and-test.json`
is that setting, written down: a GitHub **ruleset** covering the default branch which

- requires a pull request, so nothing lands on `main` by direct push;
- requires the **`Build and test`** check to pass, so the merge button stays disabled while it is
  queued, running, or failing;
- blocks deletion and force-pushes on `main`, since the deploy history is what a rollback reads;
- grants **no bypass to anyone**, including the repo owner.

**GitHub does not read this file from the repository.** Nothing in a repo can grant itself branch
protection — that would rather defeat the point. The file is an importable export, and it has to be
applied once by hand:

> *Settings → Rules → Rulesets → New ruleset → **Import a ruleset*** → upload
> `.github/rulesets/main-build-and-test.json` → **Create**.

Check it took by opening any pull request: the merge button should be greyed with *Required
statuses must pass before merging*. After that the file is the record of what is configured — change
the rule here and re-import, so the setting is reviewable in a diff like everything else.

**`Build and test` has to run on every pull request, or the guard inverts.** A required check that
never reports leaves a PR pending forever rather than mergeable, so `ci.yml` deliberately carries no
`paths:` filter — adding one later to "skip CI for docs" would silently wedge every docs-only PR.
If the job is ever renamed, the ruleset's `context` must be renamed with it.

**Deliberately not enabled: *require branches to be up to date*** (`strict_required_status_checks_policy`).
It would force every PR to re-run against a moved `main` before landing, which for a single-maintainer
repo is mostly friction. The case it protects against — a PR that passed against a stale `main` and
breaks once merged — is already caught before it can reach production, because `fly-deploy.yml` runs
the same CI workflow again on `main` and the deploy job depends on it. Turn it on if that changes.

**The escape hatch is the ruleset, not a bypass.** With `bypass_actors` empty there is no way to
merge past a red build quietly; an emergency means setting the ruleset to *Disabled*, which is a
visible, logged act that shows up in the repo's rule insights. That is the intended trade — the
guard is worth little if the person most likely to be in a hurry can step around it silently.

Manual deploys still work from the repo root:

```powershell
fly deploy
```

Either way, migrations run automatically on startup, same as locally.

## A deploy has to prove it serves

`flyctl deploy` reporting success only means the machine started. After it, the workflow requests
`https://gjs-meiden.nl/health` and fails the job unless it answers `200`. The public hostname is
used on purpose, so DNS and the certificate are part of what gets checked rather than only the
container.

`/health` (mapped in `Program.cs`) opens the database and runs a real query rather than
`CanConnectAsync`, which for SQLite only opens the file and so succeeds against a schema a
migration left half-applied. A boot that migrated badly, or refused to migrate because the backup
failed, therefore fails the deploy instead of waiting to be found by the first parent to open the
app on match day. The request is retried five times with a growing pause (5 s, 10 s, 15 s, 20 s),
because the machine is usually cold-starting from zero when it arrives.

**Deliberately a one-shot request, not a `[[http_service.checks]]` block in `fly.toml`.** Fly's
proxy health checks count towards the concurrency its autostop decision reads, so a check running
every few seconds holds the machine awake and quietly undoes scale-to-zero — the thing that keeps
this app at a few euros a month. A check that has to be paid for continuously to tell us something
we only need to know at deploy time is the wrong trade here.

Manual deploys skip the smoke step, so check it by hand after one:

```powershell
curl https://gjs-meiden.nl/health    # "healthy"
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
  elsewhere is what would close that; the roadmap's in-app database export is the same gap seen
  from the other side.
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
