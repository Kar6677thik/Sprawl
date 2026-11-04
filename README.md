# Sprawl

A minimalist infinite canvas, one per topic, that keeps growing over time. Each canvas is a living map of your thinking on one subject: regions of cards (ideas, notes, open questions, links, code) joined by labelled connections. Claude adds most of the content through an MCP server, and you edit directly in the UI. Every change goes into an operation log, so you can see how a canvas grew and revert it to any version.

**Status: M1 done; M2 (web canvas) in progress.** The API, data model, deterministic placement, op log with undo and revert, live events and tests are built. The canvas UI renders, zooms semantically, updates live and saves drags; editing, the command palette and Tidy come next. The MCP server is M3. Design: [docs/01-design.md](docs/01-design.md).

## Start locally on Windows

Prerequisites: .NET SDK **10.0.401** or a later 10.0.4xx patch (`global.json` rolls forward), Node **24.15+** with npm, and a trusted localhost development certificate (`dotnet dev-certs https --check --trust`). No cluster, issuer or tunnel is needed. The scripts run in the built-in Windows PowerShell 5.1.

```powershell
./scripts/build-local.ps1
./scripts/start-local.ps1
```

`start-local.ps1` does three things:
- creates the MCP device token and its hash under the ignored `.local` folder on first run (`scripts/new-device-token.ps1`); neither is printed
- seeds the sample *Raft consensus* canvas into an empty database
- starts the API with two listeners (open the UI listener in a browser; `build-local.ps1` puts the web app in the API's `wwwroot`):

| Listener | Address | Accepts |
|---|---|---|
| UI | `https://localhost:7543` | Owner cookie only. Development sign-in is `POST /api/v1/session/dev`, from loopback only |
| Device | `http://127.0.0.1:7544` | MCP device token only |

To try password + TOTP sign-in instead, provision files with orbit-cloud's credential tool and pass them in:

```powershell
dotnet run --project ..\orbit-cloud\tools\Orbit.Credentials -- .local\owner.verifier
dotnet run --project ..\orbit-cloud\tools\Orbit.Credentials -- --totp .local\owner.totp
./scripts/start-local.ps1 -VerifierFile .local\owner.verifier -TotpFile .local\owner.totp
```

Try the API as the MCP would, in PowerShell:

```powershell
$token = Get-Content .local\mcp-device.token
$id = (Invoke-RestMethod http://127.0.0.1:7544/api/v1/canvases -Headers @{ Authorization = "Bearer $token" })[0].id
Invoke-RestMethod "http://127.0.0.1:7544/api/v1/canvases/$id/outline" -Headers @{ Authorization = "Bearer $token" }
```

Keep `.local` private: it holds the device token, verifiers and the database.

## Projects and validation

| Location | Purpose |
|---|---|
| `src/Sprawl.Contracts` | Entities, intents and DTOs shared by the API and (from M3) the MCP |
| `src/Sprawl.Domain` | Pure logic: batch engine, deterministic placement, op diff, undo and revert, outline rendering |
| `src/Sprawl.Api` | ASP.NET Core API: SQLite store and migrations, single write path, FTS5 search, SSE, owner and device auth; serves the built SPA |
| `src/Sprawl.Web` | Vite + React + TypeScript canvas: React Flow, semantic zoom, live SSE, light and dark themes; Vitest for the logic |
| `tests/Sprawl.Domain.Tests` | Placement goldens and randomized invariants, batches, history, outline |
| `tests/Sprawl.Api.Tests` | Endpoints in-process: concurrency (412/428), idempotency, atomicity, snapshots, trash, SSE, auth on both listeners |
| `samples/seed` | The sample canvas |
| `scripts` | Local build, start and device-token scripts (PowerShell 5.1) |

```powershell
dotnet restore Sprawl.slnx --locked-mode
dotnet build Sprawl.slnx -c Release --no-restore
dotnet test Sprawl.slnx -c Release --no-build
Push-Location src/Sprawl.Web
npm ci --no-audit --no-fund
npm test
npm run build
Pop-Location
```

Dependency lock files are checked in. Selected versions: .NET SDK **10.0.401** (rolls forward to the latest 10.0.4xx patch), ASP.NET Core and Microsoft.Data.Sqlite **10.0.14**, xUnit **2.9.3**, Microsoft.NET.Test.Sdk **18.11.0**, Node **24.15.0**, React **19.3.1**, @xyflow/react **12.12.1**, elkjs **0.12.1**, Vite **8.3.5**, TypeScript **7.0.2**, Vitest **5.1.0**.
