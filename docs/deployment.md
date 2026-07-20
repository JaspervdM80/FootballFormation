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
Manual deploys still work from the repo root:

```powershell
fly deploy
```

Either way, migrations run automatically on startup, same as locally.

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
