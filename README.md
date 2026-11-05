# Sprawl

A minimalist infinite canvas, one per topic, that keeps growing over time. Each canvas is a living map of your thinking on one subject: regions of cards (ideas, notes, open questions, links, code) joined by labelled connections. Claude adds most of the content through an MCP server, and you edit directly in the UI. Every change goes into an operation log, so you can see how a canvas grew and revert it to any version.

**Status: M1–M5 done and live on Megatron; feature plan P1–P5 built ([docs/03-features.md](docs/03-features.md)): links, tags, backlinks and insights; export, import and present mode; inbox and question review; links between canvases and collections; history replay and saved searches.** The API (data model, deterministic placement, op log with undo and revert, live events), the web canvas (semantic zoom, live updates, editing, command palette, Tidy, history, `[[links]]`, `#tags`, backlinks, insights) and the MCP server (18 tools, 7 prompts, OAuth resource server). Runbook: [docs/02-operations.md](docs/02-operations.md). What comes next and why: [docs/03-features.md](docs/03-features.md). In the canvas, press `?` for keyboard shortcuts. Design: [docs/01-design.md](docs/01-design.md).

## Start locally on Windows

Prerequisites: .NET SDK **10.0.401** or a later 10.0.4xx patch (`global.json` rolls forward), Node **24.15+** with npm, and a trusted localhost development certificate (`dotnet dev-certs https --check --trust`). No cluster, issuer or tunnel is needed. The scripts run in the built-in Windows PowerShell 5.1.

```powershell
./scripts/build-local.ps1
./scripts/start-local.ps1
```

`start-local.ps1` does four things:
- creates the MCP device token and its hash, and a local MCP bearer, under the ignored `.local` folder on first run; none is printed
- seeds the sample *Raft consensus* canvas into an empty database
- starts the API and the MCP server together (Ctrl+C stops both); `build-local.ps1` puts the web app in the API's `wwwroot`
- writes `.local/claude-mcp.json`, an MCP client config holding the local bearer

| Listener | Address | Accepts |
|---|---|---|
| UI | `https://localhost:7543` | Owner cookie only. Open it in a browser and use **Development sign-in** (loopback only) |
| Device | `http://127.0.0.1:7544` | MCP device token only |
| MCP | `http://localhost:5181/mcp` | The local bearer in `.local/mcp-dev.token` (Development only; production uses Orbit Identity OAuth) |

Use the MCP server from Claude Code or MCP Inspector with the generated config, so the bearer never goes on a command line:

```powershell
claude --mcp-config .local\claude-mcp.json --strict-mcp-config
npx @modelcontextprotocol/inspector@2.13.0 --cli --config .local\claude-mcp.json --server sprawl --method tools/list
```

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
| `src/Sprawl.Mcp` | Stateless MCP server: 18 tools, 7 prompts, OAuth resource server for Orbit Identity, calls the API with its own device token |
| `tests/Sprawl.Domain.Tests` | Placement goldens and randomized invariants, batches, history, outline |
| `tests/Sprawl.Api.Tests` | Endpoints in-process: concurrency (412/428), idempotency, atomicity, snapshots, trash, SSE, auth on both listeners |
| `tests/Sprawl.Mcp.Tests` | Scopes and tool discovery, protected-resource metadata, token rejection (audience, issuer, type, algorithm, subject), dev bearer refused outside Development, a full tool session against the real API |
| `samples/seed` | The sample canvas |
| `scripts` | Local build and start, device token, and release image build (PowerShell 5.1) |
| `deploy` | k3s manifests for the API (with backup and restore jobs), the MCP's Orbit catalog spec, and the image import helper |
| `Dockerfile`, `Dockerfile.mcp` | Release images: digest-pinned, non-root, read-only root filesystem |

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

Dependency lock files are checked in. Selected versions: .NET SDK **10.0.401** (rolls forward to the latest 10.0.4xx patch), ASP.NET Core and Microsoft.Data.Sqlite **10.0.14**, ModelContextProtocol.AspNetCore **2.3.1**, xUnit **2.9.3**, Microsoft.NET.Test.Sdk **18.11.0**, Node **24.15.0**, React **19.3.1**, @xyflow/react **12.12.1**, elkjs **0.12.1**, Vite **8.3.5**, TypeScript **7.0.2**, Vitest **5.1.0**.
