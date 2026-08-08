# Deployment — Fly.io + gjs-meiden.nl

The app is deployed to [Fly.io](https://fly.io) (region `ams`, Amsterdam) as a Docker container
with the custom domain **gjs-meiden.nl**. Chosen because Blazor Server needs WebSockets and
SQLite needs a persistent disk, and Fly provides both plus free TLS certificates for
custom domains at the lowest price point (~$3–5/month, less with scale-to-zero).

## Moving parts

| File | Purpose |
|------|---------|
| `Dockerfile` | Multi-stage build (SDK → aspnet runtime), listens on 8080 |
| `fly.toml` | App `gjs-meiden`, volume `data` mounted at `/data`, scale-to-zero enabled |
| `Program.cs` | `APP_DATA_DIR` env var overrides the data folder (DB, logs, data-protection keys) |

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

The same workflow is the pull request gate: **every** pull request runs `build` — restore, a
Release build (where warnings are errors) and the test suite — and the `deploy` job is skipped,
so a PR can never reach the volume. Deploying needs both a non-PR event *and* `main`, which is
what stops a `workflow_dispatch` run against a feature branch from putting that branch into
production. Only the deploy job is serialised (`concurrency: fly-deploy`); PR checks are keyed by
ref, so they start straight away and a new push cancels the run it supersedes.

Manual deploys still work from the repo root:

```powershell
fly deploy
```

Either way, migrations run automatically on startup, same as locally.

## The database is snapshotted before every migration

Startup does three things in order, in `Program.cs` (see `Core/Data/DatabaseSafety.cs`):

1. **If — and only if — migrations are pending**, copy the database to
   `/data/backups/pre-migration-<utc timestamp>.db`, keeping the newest 5. The copy is taken with
   SQLite's own backup API rather than a file copy, because with WAL journalling the `.db` file on
   its own can be missing the most recent writes.
2. Apply the migrations.
3. `PRAGMA integrity_check` and `PRAGMA foreign_key_check`. Either failing throws, so a damaged
   database stops the boot loudly instead of serving wrong answers.

**A failed backup aborts the migration.** That is deliberate: several migrations are one-way in
practice (`AddMatchTypeAndComments` drops a column, `AddMustChangePasswordAndLineupUniqueIndex`
deletes rows), so once one has run without a snapshot there is no route back. A container that
refuses to start is an afternoon's problem; a season of lineups quietly rewritten is permanent.
If the app will not boot for this reason, the volume is full — that is the thing to fix.

To recover from a bad migration: stop the machine, replace `/data/footballformation.db` with the
newest `pre-migration-*.db`, and deploy the previous image.

```powershell
fly ssh console -C "ls -la /data/backups"                  # what snapshots exist
fly ssh sftp get /data/backups/pre-migration-<stamp>.db    # pull one down
```

Snapshots are pre-migration only. They are not a substitute for a routine backup of a database
that changes every match day — take one of those before anything risky.

## Useful commands

```powershell
fly logs                 # live server logs (Serilog console output)
fly status               # machine state (stopped = scaled to zero, normal)
fly ssh console          # shell inside the container
fly ssh sftp get /data/footballformation.db backup.db   # DB backup
```

## Cost control

- `auto_stop_machines = "stop"` + `min_machines_running = 0`: the VM stops when no one
  is connected and auto-starts on the next request (~2–5 s cold start). A team app used
  a few hours a week mostly costs storage (~$0.15/GB/month) plus pennies of runtime.
- Single 512 MB shared-CPU machine ≈ $3–4/month if it ran 24/7 — treat that as the ceiling.
- **Do not** scale to more than one machine: SQLite lives on one volume; a second machine
  would get its own empty volume and a split-brain database.
