# Sprawl operations

Installing, running and recovering Sprawl on Megatron. Written 5 November 2026 for the M5 release. **Status: rehearsed end to end on a disposable k3s 1.36.4 (the same image Megatron runs); not yet applied to Megatron.** Design: [01-design.md](01-design.md) §10.

| Part | Where | Reached at |
|---|---|---|
| UI + API (`sprawl`) | Namespace `sprawl`, Service `10.43.0.102`, SQLite on PVC `sprawl-data` | `https://sprawl.tailf67d61.ts.net` (tailnet only, `svc:sprawl`) |
| MCP (`sprawl-mcp`) | Orbit catalog app, namespace `app-sprawl-mcp` | `https://canvas-mcp.karthiksurkanti.in/mcp` (Cloudflare tunnel `main`, path-limited) |
| OAuth | Orbit Identity (already live; orbit-cloud PR #6) | `https://auth.karthiksurkanti.in/` |
| Backups | API writes verified copies to `/data/backups` nightly; CronJob `sprawl-backup` ships them to restic | Your restic repository |

Megatron conventions (from Orbit): `ssh kar_thik@100.91.115.46`. Every privileged step runs inside **one** `sudo -S sh -c "…"`, because a second `sudo` over non-interactive SSH does not reuse the first one's authentication. Never put a password, token or verifier on a command line; secrets go into files and `--from-file`.

## 0. Before you start

- Orbit Identity serves the Sprawl resource. Check: `curl -s https://auth.karthiksurkanti.in/.well-known/oauth-authorization-server | grep -o '"canvas\.[a-z]*"'` lists four scopes.
- `10.43.0.102` is free: `kubectl get svc -A -o wide | grep '10.43.0.10'` shows only `.100` (Orbit) and `.101` (Identity).
- On the PC: Docker Desktop (Linux engine), the Sprawl repository built and tested (`./scripts/build-local.ps1`, `dotnet test Sprawl.slnx`).

## 1. Build the images (PC)

```powershell
./scripts/build-images.ps1        # → .local\images\sprawl.tar, .local\images\sprawl-mcp.tar (linux/amd64)
```

Both images are digest-pinned multi-stage builds, run as the numeric non-root user 1654, and contain no SDK, Node or source.

## 2. Import on Megatron and record the digests

```powershell
scp .local\images\sprawl.tar .local\images\sprawl-mcp.tar deploy\import-image.sh kar_thik@100.91.115.46:/tmp/
```

```sh
$ sudo -S sh -c 'sh /tmp/import-image.sh ghcr.io/kar6677thik/sprawl /tmp/sprawl.tar; sh /tmp/import-image.sh ghcr.io/kar6677thik/sprawl-mcp /tmp/sprawl-mcp.tar; rm -f /tmp/sprawl.tar /tmp/sprawl-mcp.tar /tmp/import-image.sh'
```

The two lines it prints are `API_DIGEST` and `MCP_DIGEST`. `import-image.sh` reads the `@sha256:` name that `ctr import --digests` registers, which is what kubelet resolves, and refuses to print anything it cannot verify. Do **not** use the digest `docker build` printed, nor the `:candidate` tag's digest. The rehearsal tried that and the pod sat in `ImagePullBackOff`.

## 3. Credentials and Secrets

Make production credentials on the PC under `.local\prod` (ignored), separate from Orbit's and from local development:

```powershell
New-Item -ItemType Directory .local\prod -Force | Out-Null
dotnet run --project ..\orbit-cloud\tools\Orbit.Credentials -- .local\prod\owner.verifier          # asks for a new password
dotnet run --project ..\orbit-cloud\tools\Orbit.Credentials -- --totp .local\prod\owner.totp       # shows the enrolment key once
./scripts/new-device-token.ps1 -TokenFile .local\prod\device.token -HashFile .local\prod\device.sha256
```

Copy them over, create the Secrets from files, and delete the copies:

```powershell
scp .local\prod\owner.verifier .local\prod\owner.totp .local\prod\device.token .local\prod\device.sha256 kar_thik@100.91.115.46:/tmp/
```

```sh
$ kubectl create namespace sprawl --dry-run=client -o yaml | kubectl apply -f -     # the manifest also creates it
$ kubectl -n sprawl create secret generic sprawl-owner --from-file=verifier=/tmp/owner.verifier --from-file=totp=/tmp/owner.totp
$ kubectl -n sprawl create secret generic sprawl-device --from-file=sha256=/tmp/device.sha256
$ shred -u /tmp/owner.verifier /tmp/owner.totp /tmp/device.sha256      # device.token stays for §5, then is shredded too
```

The API holds only the token's SHA-256. The token itself goes only to the MCP (§5).

## 4. The API and UI

```sh
$ sed "s/IMAGE_DIGEST/<API_DIGEST>/" deploy/api/sprawl.yaml | kubectl apply -f -
$ kubectl -n sprawl rollout status deploy/sprawl --timeout=150s
```

**Tailnet.** Following orbit-cloud `deploy/tailscale.md`, add a grant and an approver for `svc:sprawl` to the policy file next to `svc:orbit`, `{ "src": ["OWNER@github"], "dst": ["svc:sprawl"], "ip": ["tcp:443"] }` and `"svc:sprawl": ["tag:megatron"]` under `autoApprovers.services`. Then:

```sh
$ sudo tailscale serve --service=svc:sprawl --https=443 http://10.43.0.102:8080
$ tailscale serve status        # svc:sprawl → http://10.43.0.102:8080; existing routes unchanged
```

What the manifest enforces:
- **Pod security:** Restricted (non-root, read-only root, no capabilities, no service-account token), `Recreate` with one replica (one SQLite writer), and a 2 Gi `local-path` PVC.
- **Network:** port 8080 is open only to the node (tailscale serve, kubelet probes), port 8081 only to `app-sprawl-mcp`, and there is no egress at all.

The rehearsal showed the limit of that: NetworkPolicy always allows a pod's own node, so processes *on Megatron* can reach 8081. They still need the device token, and the ClusterIP is not reachable from the LAN or the tailnet.

## 5. The MCP (Orbit catalog app)

Hand the Orbit Cloud thread (`6bce92e7-bc39-4ac3-baed-c8c0ff58fff1`) [deploy/mcp/sprawl-mcp.yaml](../deploy/mcp/sprawl-mcp.yaml) with `IMAGE_DIGEST` replaced by `MCP_DIGEST`. Ask it to:

1. Add the spec as `catalog/apps/sprawl-mcp.yaml` in a reviewed PR. The rehearsal validated it with `orbit-catalog validate` and rendered it with `render`.
2. Apply the onboarding kit (`orbit-catalog onboard catalog sprawl-mcp`), and the admission policy if `policy.yaml` changed.
3. Create the Secret in the app namespace, from the file left in §3:
   `kubectl -n app-sprawl-mcp create secret generic sprawl-mcp-secrets --from-file=device-token=/tmp/device.token && shred -u /tmp/device.token`
4. Release with its usual `app-bundle.yml` + `deploy-app.ps1` flow.

The catalog accepts only upper-case variable names, so the spec uses `MCP__APIBASEURL` and similar. .NET configuration is case-insensitive, and the rehearsal ran the container with exactly those names.

Check: `kubectl -n app-sprawl-mcp rollout status deploy/sprawl-mcp`, then from Megatron `curl -s http://$(kubectl -n app-sprawl-mcp get svc sprawl-mcp -o jsonpath='{.spec.clusterIP}')/health/ready` returns `{"status":"Ready"}`. That proves the MCP reaches the API's device port through both network policies with the device token.

## 6. Public route for the MCP

```sh
$ cloudflared tunnel route dns main canvas-mcp.karthiksurkanti.in
$ MCP_IP=$(kubectl -n app-sprawl-mcp get svc sprawl-mcp -o jsonpath='{.spec.clusterIP}'); echo $MCP_IP
$ cp ~/.cloudflared/config.yml ~/.cloudflared/config.yml.pre-sprawl-$(date +%Y%m%d-%H%M%S)
$ nano ~/.cloudflared/config.yml
```

Add these rules **directly above** the final `- service: http_status:404`:

```yaml
  # Sprawl MCP: the MCP endpoint and its protected-resource metadata only.
  - hostname: canvas-mcp.karthiksurkanti.in
    path: ^/(mcp|\.well-known/oauth-protected-resource(/mcp)?)$
    service: http://MCP_IP:80

  - hostname: canvas-mcp.karthiksurkanti.in
    service: http_status:404
```

```sh
$ cloudflared tunnel ingress validate
$ cloudflared tunnel ingress rule https://canvas-mcp.karthiksurkanti.in/mcp             # → the MCP_IP rule
$ cloudflared tunnel ingress rule https://canvas-mcp.karthiksurkanti.in/health/ready    # → http_status:404
$ systemctl --user restart cloudflared-main-slate-web.service && systemctl --user is-active cloudflared-main-slate-web.service
```

The catalog Service's ClusterIP is not fixed. It stays stable while the Service exists, and the catalog's deployer cannot delete Services. If it ever changes, the tunnel answers 502 (fails closed); update `MCP_IP`.

Cloudflare: no bot challenge or WAF block on `canvas-mcp.` (Claude's servers call it from `160.79.104.0/21`).

## 7. Backups

What happens every night:
- **02:00 UTC:** the API writes a consistent copy with `VACUUM INTO` while running. It integrity-checks the copy, keeps the newest 14 in `/data/backups`, and records `backup-status.json`.
- **02:30 UTC:** CronJob `sprawl-backup` runs as the non-root restic user. It refuses to run if the last copy failed, then backs up `/data/backups` to restic, prunes (7 daily, 4 weekly, 6 monthly) and runs `restic check`.

Setup:

```sh
$ kubectl -n sprawl create secret generic sprawl-backup --from-file=RESTIC_REPOSITORY=/tmp/repo --from-file=RESTIC_PASSWORD=/tmp/pass \
    --from-file=AWS_ACCESS_KEY_ID=/tmp/key-id --from-file=AWS_SECRET_ACCESS_KEY=/tmp/key && shred -u /tmp/repo /tmp/pass /tmp/key-id /tmp/key
$ kubectl apply -f deploy/api/backup.yaml
$ kubectl -n sprawl create job --from=cronjob/sprawl-backup sprawl-backup-first && kubectl -n sprawl logs -f job/sprawl-backup-first
```

The first run creates the repository. Keep the repository password in your password manager: without it the backups are unreadable.

- **Backend:** the job may reach HTTPS (443) on public addresses only; for another backend port, edit the policy in `backup.yaml`.
- **Before an upgrade or a restore drill:** Ctrl+K → **Back up now** in the UI makes a fresh verified copy. Then run a job from the CronJob as above.

## 8. Acceptance

Record the time and result for each row. Never record tokens.

| From | Check | Expected |
|---|---|---|
| Tailnet device | `https://sprawl.tailf67d61.ts.net` | Sign-in page with a valid certificate; password + TOTP opens the canvas list |
| Phone off the tailnet | Same URL | Does not resolve or connect |
| Anywhere | `https://canvas-mcp.karthiksurkanti.in/.well-known/oauth-protected-resource` | JSON: `resource` is the exact `/mcp` URL; one authorization server, `https://auth.karthiksurkanti.in/` |
| Anywhere | `POST https://canvas-mcp.karthiksurkanti.in/mcp` without a token | 401 with `WWW-Authenticate: Bearer resource_metadata=…` |
| Anywhere | `https://canvas-mcp.karthiksurkanti.in/health/ready` | 404 (blocked at the tunnel) |
| Megatron | `kubectl -n sprawl get cronjob,pvc; kubectl -n app-sprawl-mcp get deploy` | All present; pods Ready |
| Your devices | Slate, `mcp.karthiksurkanti.in`, Orbit, Identity | Unchanged |

## 9. Linking Claude

In Claude, add a **new** custom connector with URL `https://canvas-mcp.karthiksurkanti.in/mcp`.
- Sign-in and consent happen on `https://auth.tailf67d61.ts.net`, so Tailscale must be on.
- Consent names *Sprawl*, with read, write and organize ticked and admin unticked. Tick admin only if you want Claude to delete.
- Then ask Claude to list your canvases. The audit log in Orbit → Identity shows the consent, with no `resource_defaulted` entry for this connection.

For Claude Code, `claude mcp add --transport http sprawl https://canvas-mcp.karthiksurkanti.in/mcp` goes through the same OAuth.

## 10. Restore (and a quarterly drill)

```sh
$ kubectl -n sprawl scale deploy/sprawl --replicas=0 && kubectl -n sprawl wait --for=delete pod -l app=sprawl --timeout=120s
$ kubectl apply -f deploy/api/restore.yaml && kubectl -n sprawl wait --for=condition=complete job/sprawl-restore --timeout=600s
$ kubectl -n sprawl logs job/sprawl-restore && kubectl -n sprawl delete job sprawl-restore
$ kubectl -n sprawl scale deploy/sprawl --replicas=1 && kubectl -n sprawl rollout status deploy/sprawl
```

- **Which copy:** by default the newest copy in the latest snapshot. Set `BACKUP_FILE` in `restore.yaml` for another.
- **Nothing is lost:** the current database is moved to `/data/pre-restore/`, never deleted.
- **What the rehearsal proved:** a canvas created after the backup was gone after the restore, and the backed-up one was back.

## 11. Upgrades

1. Ctrl+K → **Back up now**.
2. Build (§1) and import (§2).
3. Apply `deploy/api/sprawl.yaml` with the new `API_DIGEST`. `Recreate` means a few seconds of downtime, and the restart signs you out (cookie keys live only in memory, by design).
4. For the MCP, ask the Orbit thread to release the new digest.

Database migrations run at startup and only ever add. If an upgrade misbehaves, apply the previous digest. If data must go back too, restore (§10).

## 12. Rollback or uninstall

```sh
$ sudo tailscale serve --service=svc:sprawl --https=443 off
$ cp ~/.cloudflared/config.yml.pre-sprawl-* ~/.cloudflared/config.yml && systemctl --user restart cloudflared-main-slate-web.service
$ kubectl delete namespace sprawl          # deletes the PVC and the database: back up first (§7)
```

To remove the MCP, the Orbit thread reverts the catalog spec and deletes `app-sprawl-mcp`. Then delete the `canvas-mcp` DNS record. Revoke the Sprawl connection from Orbit → Identity. The `canvas.*` resource can stay configured in Identity; without the MCP it is unused.

## Installation record

Not installed yet. Record the date, digests, ClusterIPs, tunnel backup file name and acceptance results here.
