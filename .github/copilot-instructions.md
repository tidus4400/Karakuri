# GitHub Copilot Instructions — tidus4400.Karakuri

## Project overview

**tidus4400.Karakuri** is a local MVP automation platform (simplified PowerApps/Alteryx-style flow orchestration) built with .NET 10.

## Solution structure

```
tidus4400.Karakuri.sln
src/
  tidus4400.Karakuri.Shared        # DTOs, enums, flow models, HMAC helper
  tidus4400.Karakuri.Orchestrator  # Minimal API + SignalR hub + EF persistence + auth
  tidus4400.Karakuri.Web           # Blazor Server UI (custom CSS only, no UI libs)
  tidus4400.Karakuri.Runner        # Runner agent (worker/console, HMAC pull protocol)
tests/
  tidus4400.Karakuri.Shared.Tests
  tidus4400.Karakuri.Orchestrator.Tests
  tidus4400.Karakuri.Runner.Tests
docker/
  Dockerfile.orchestrator
  Dockerfile.web
  Dockerfile.runner
docker-compose.yml
```

## Coding conventions

- Target framework: **net10.0**
- Namespaces follow directory structure: `tidus4400.Karakuri.<Project>[.<Subfolder>]`
- No external UI libraries — Blazor UI uses custom CSS only (no Bootstrap, Tailwind, MudBlazor, etc.)
- Minimal API style for all orchestrator endpoints (no controllers)
- Use `record` types for DTOs in `Shared`
- Use `async`/`await` throughout; cancellation tokens passed where relevant
- All runner↔orchestrator HTTP requests use HMAC authentication (`X-Agent-Id`, `X-Timestamp`, `X-Signature`)

## Key architecture points

### Orchestrator (`tidus4400.Karakuri.Orchestrator`)
- Two EF DbContexts: `AuthIdentityDbContext` (Identity/auth) and `PlatformDbContext` (domain: flows, jobs, runners, tokens, logs)
- Both use **PostgreSQL** (Npgsql) in production; **SQLite** is supported via `Auth:Provider=Sqlite` / `Domain:Provider=Sqlite` env vars (used in tests)
- Both migrations are applied on startup via `Database.Migrate()`
- Domain state is currently accessed through `AppStore.cs` — an EF-backed facade that wraps `PlatformDbContext`. It still uses a full-snapshot rewrite pattern (MVP simplification)
- SignalR hub at `/hubs/monitoring` for live UI updates
- Endpoints require cookie auth; HMAC auth is used for runner-facing endpoints
- API returns `403` (not redirect) for access-denied on `/api/*` paths

### Runner (`tidus4400.Karakuri.Runner`)
- Registers via `POST /api/agents/register` using a one-time token; stores `AgentId` + `AgentSecret` in `runner.credentials.json`
- Long-polls orchestrator for jobs, executes `RunProcess` steps sequentially
- Sends HMAC-signed heartbeats, job events, logs, and completion callbacks
- Polls `GET /api/jobs/{jobId}/cancel-status` while executing and kills the process on cancellation
- Local Kestrel endpoints: `GET /health`, `/version`, `/status` on `127.0.0.1:5180`

### HMAC signing (canonical format)
```
{METHOD}\n{PATH}\n{TIMESTAMP}\n{SHA256_HEX_OF_BODY}
```
Signed with `HMACSHA256(secret, canonicalString)`. Timestamp window: ±300s.

### Web (`tidus4400.Karakuri.Web`)
- Blazor Server with SignalR client (`MonitoringHubClient`) for live dashboard/job/runner updates
- Uses `X-User-*` header fallback for cross-process dev auth to SignalR hub
- `OrchestratorApiClient` wraps all HTTP calls to the orchestrator

## Build & test commands

```bash
# Build
dotnet build tidus4400.Karakuri.sln -p:NuGetAudit=false

# Test (22 tests: 10 shared, 4 runner, 8 orchestrator)
dotnet test tidus4400.Karakuri.sln -p:NuGetAudit=false

# Run orchestrator
dotnet run --project src/tidus4400.Karakuri.Orchestrator --urls http://localhost:5010

# Run web
dotnet run --project src/tidus4400.Karakuri.Web --urls http://localhost:5020

# Run runner
dotnet run --project src/tidus4400.Karakuri.Runner

# EF migrations
dotnet tool restore
dotnet dotnet-ef migrations list --project src/tidus4400.Karakuri.Orchestrator --startup-project src/tidus4400.Karakuri.Orchestrator --context AuthIdentityDbContext
dotnet dotnet-ef migrations list --project src/tidus4400.Karakuri.Orchestrator --startup-project src/tidus4400.Karakuri.Orchestrator --context PlatformDbContext
```

## Seeded credentials

| Role  | Email         | Password  |
|-------|---------------|-----------|
| Admin | admin@local   | Admin123! |
| User  | user@local    | User123!  |

## Known gaps / next steps

- `AppStore.cs` should be replaced with direct `PlatformDbContext` usage in each endpoint (retire snapshot rewrite)
- Only `RunProcess` block type is implemented in the runner
- No bulk cancel/retry on the jobs list page yet
- `SecretValue` is stored in plaintext in the domain DB (MVP simplification)
- Web auth uses `X-User-*` header fallback for SignalR (replace with proper cookie forwarding)
