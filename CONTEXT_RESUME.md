# AutomationPlatform Resume Context (for Codex / GH Copilot)

Last updated: 2026-02-24
Workspace: `/Users/tidus4400/Projects/Karakuri`

## Current status

Implemented and compiling:
- `AutomationPlatform.Shared`
- `AutomationPlatform.Orchestrator` (Minimal API + SignalR hub + auth)
- `AutomationPlatform.Web` (Blazor Server UI, custom CSS Grid UI)
- `AutomationPlatform.Runner` (runner agent implemented: registration + HMAC + polling + RunProcess + local endpoints)
- `tests/*` (new unit + integration test suite)

## Major features completed

- Flow/job/runner API surface for MVP (manual runs, runner pull model)
- Runner HMAC signing/validation (`X-Agent-Id`, `X-Timestamp`, `X-Signature`)
- Runner registration token flow
- SignalR hub on orchestrator: `/hubs/monitoring`
- Blazor UI pages:
  - `/dashboard`
  - `/flows`
  - `/flows/{id}/builder`
  - `/jobs`
  - `/jobs/{id}`
  - `/runners`
  - `/tokens`
  - `/blocks`
  - `/login`
- Custom CSS only (no Bootstrap/Tailwind/Mud/etc)
- Identity auth persistence switched to ASP.NET Core Identity + EF (`Npgsql` provider for runtime)
- Blazor web switched to SignalR client for live updates (dashboard/jobs/runners/job details)
- Identity EF migration scaffolding added
- Automated tests added across shared/orchestrator/runner projects
- Runner agent implementation added (registration, HMAC signing, heartbeat, long-poll, `RunProcess` execution, local status endpoints)
- End-to-end cancel support added (user cancel request endpoint, runner cancel polling + process kill + canceled completion endpoint, UI cancel action)

## Testing suite (added)

Test projects:
- `/Users/tidus4400/Projects/Karakuri/tests/AutomationPlatform.Shared.Tests`
- `/Users/tidus4400/Projects/Karakuri/tests/AutomationPlatform.Runner.Tests`
- `/Users/tidus4400/Projects/Karakuri/tests/AutomationPlatform.Orchestrator.Tests`

Coverage highlights:
- Shared unit tests:
  - HMAC signing/hash helpers
  - JSON config helper parsing (`string`, `int`, `JsonElement`)
  - `FlowDefinition.CreateDefault()`
- Runner unit tests:
  - `RunProcessExecutor` success case
  - `RunProcessExecutor` timeout behavior
  - bounded stdout capture/truncation
  - external cancellation token path (kills process / throws cancellation)
- Orchestrator tests:
  - `FlowOrdering` topo sort + cycle fallback
  - integration tests with `WebApplicationFactory<Program>`
  - SQLite-backed Identity auth DB in tests
  - temp JSON `AppStore` path per test host
  - seeded admin/user login (`/api/auth/login`, `/api/auth/me`)
  - user scoping for `/api/flows`
  - admin-only protection (`/api/tokens`, `/api/runners`)
  - runner registration token flow
  - HMAC heartbeat validation
  - flow version save, manual run, runner pull
  - job events + complete + details/log paging
  - cancel flow: user cancel request -> runner cancel-status poll -> runner canceled callback -> job/steps/logs show canceled

Current test command/status:
```bash
dotnet test /Users/tidus4400/Projects/Karakuri/AutomationPlatform.sln -p:NuGetAudit=false
```
- Passes: `20` tests total (`10` shared, `3` runner, `7` orchestrator)
- Passes: `22` tests total (`10` shared, `4` runner, `8` orchestrator)

Latest coverage snapshots (Cobertura, local run on 2026-02-24):
- Shared tests package coverage (`AutomationPlatform.Shared`): line-rate `0.4009`, branch-rate `0.8333`
- Runner tests package coverage (`AutomationPlatform.Runner`): line-rate `0.1225`, branch-rate `0.1193`
- Orchestrator tests package coverage (`AutomationPlatform.Orchestrator`): line-rate `0.5020`, branch-rate `0.4784`

## Important remaining gaps (still fallback / incomplete)

Domain persistence (flows/jobs/runners/tokens/logs) is still JSON-file based via:
- `/Users/tidus4400/Projects/Karakuri/src/AutomationPlatform.Orchestrator/AppStore.cs`

Identity/auth persistence is EF/PostgreSQL, but core domain persistence is not yet EF-backed.

Runner is implemented for the MVP pull protocol, but gaps remain:
- only built-in `RunProcess` block is supported
- cancellation is now end-to-end for running `RunProcess` steps via cancel polling, but there is still no dedicated UI/endpoint for bulk canceling queued/running jobs from the jobs list page

## Key files

- Shared contracts/helpers:
  - `/Users/tidus4400/Projects/Karakuri/src/AutomationPlatform.Shared`
- Orchestrator startup/endpoints:
  - `/Users/tidus4400/Projects/Karakuri/src/AutomationPlatform.Orchestrator/Program.cs`
- Orchestrator JSON domain store (fallback):
  - `/Users/tidus4400/Projects/Karakuri/src/AutomationPlatform.Orchestrator/AppStore.cs`
- Identity EF DbContext:
  - `/Users/tidus4400/Projects/Karakuri/src/AutomationPlatform.Orchestrator/IdentityPersistence.cs`
- Identity design-time DbContext factory:
  - `/Users/tidus4400/Projects/Karakuri/src/AutomationPlatform.Orchestrator/AuthIdentityDbContextFactory.cs`
- Identity migrations:
  - `/Users/tidus4400/Projects/Karakuri/src/AutomationPlatform.Orchestrator/Migrations/Auth`
- Web API client:
  - `/Users/tidus4400/Projects/Karakuri/src/AutomationPlatform.Web/Services/OrchestratorApiClient.cs`
- Web SignalR client service:
  - `/Users/tidus4400/Projects/Karakuri/src/AutomationPlatform.Web/Services/MonitoringHubClient.cs`
- Runner core:
  - `/Users/tidus4400/Projects/Karakuri/src/AutomationPlatform.Runner/Program.cs`
  - `/Users/tidus4400/Projects/Karakuri/src/AutomationPlatform.Runner/Worker.cs`
  - `/Users/tidus4400/Projects/Karakuri/src/AutomationPlatform.Runner/RunnerEngine.cs`
  - `/Users/tidus4400/Projects/Karakuri/src/AutomationPlatform.Runner/OrchestratorClient.cs`
  - `/Users/tidus4400/Projects/Karakuri/src/AutomationPlatform.Runner/RunProcessExecutor.cs`
  - `/Users/tidus4400/Projects/Karakuri/src/AutomationPlatform.Runner/RunnerCredentialStore.cs`
- Test harness / integration factory:
  - `/Users/tidus4400/Projects/Karakuri/tests/AutomationPlatform.Orchestrator.Tests/TestOrchestratorFactory.cs`
  - `/Users/tidus4400/Projects/Karakuri/tests/AutomationPlatform.Orchestrator.Tests/OrchestratorApiIntegrationTests.cs`
- Web job details cancel UI:
  - `/Users/tidus4400/Projects/Karakuri/src/AutomationPlatform.Web/Components/Pages/JobDetails.razor`

## Package state (NuGet now available)

Added packages:
- Orchestrator:
  - `Microsoft.EntityFrameworkCore`
  - `Microsoft.AspNetCore.Identity.EntityFrameworkCore`
  - `Microsoft.EntityFrameworkCore.Design`
  - `Microsoft.EntityFrameworkCore.Sqlite` (used for tests / optional auth provider switch)
  - `Npgsql.EntityFrameworkCore.PostgreSQL`
- Web:
  - `Microsoft.AspNetCore.SignalR.Client`
- Tests:
  - `Microsoft.AspNetCore.Mvc.Testing` (orchestrator integration tests)
  - `Microsoft.EntityFrameworkCore.Sqlite` (test project)

Local EF tool manifest exists:
- `/Users/tidus4400/Projects/Karakuri/dotnet-tools.json`

## Startup behavior

- Orchestrator runs Identity migrations on startup (`Database.Migrate()` for `AuthIdentityDbContext`)
- `Auth:Provider=Sqlite` is supported (primarily for tests); default remains PostgreSQL/Npgsql
- Orchestrator still initializes JSON domain store (`AppStore`) for flows/jobs/runners/tokens/logs
- Web connects to orchestrator SignalR hub using header-based auth fallback (`X-User-*`) from current UI session state
- API cookie auth now returns proper `403` for `/api/*` access-denied instead of redirecting to a missing page
- Runner HMAC validation now works for JSON-body POSTs because request buffering is enabled before model binding and the validator rewinds before hashing
- Orchestrator now supports:
  - `POST /api/jobs/{id}/cancel` (user/admin cancel request)
  - `GET /api/jobs/{jobId}/cancel-status` (runner HMAC)
  - `POST /api/jobs/{jobId}/canceled` (runner HMAC canceled completion)
- Runner now polls cancel status while executing `RunProcess` and kills the process on cancel request

## Commands used frequently

Build projects:
```bash
dotnet build /Users/tidus4400/Projects/Karakuri/src/AutomationPlatform.Orchestrator/AutomationPlatform.Orchestrator.csproj -p:NuGetAudit=false
dotnet build /Users/tidus4400/Projects/Karakuri/src/AutomationPlatform.Web/AutomationPlatform.Web.csproj -p:NuGetAudit=false
dotnet build /Users/tidus4400/Projects/Karakuri/src/AutomationPlatform.Runner/AutomationPlatform.Runner.csproj -p:NuGetAudit=false
dotnet test /Users/tidus4400/Projects/Karakuri/AutomationPlatform.sln -p:NuGetAudit=false
```

Run apps:
```bash
dotnet run --project /Users/tidus4400/Projects/Karakuri/src/AutomationPlatform.Orchestrator --urls http://localhost:5010
dotnet run --project /Users/tidus4400/Projects/Karakuri/src/AutomationPlatform.Web --urls http://localhost:5020
dotnet run --project /Users/tidus4400/Projects/Karakuri/src/AutomationPlatform.Runner
```

EF tools:
```bash
cd /Users/tidus4400/Projects/Karakuri
dotnet tool restore
dotnet dotnet-ef migrations list --project src/AutomationPlatform.Orchestrator --startup-project src/AutomationPlatform.Orchestrator --context AuthIdentityDbContext
```

## Recommended next engineering steps

1. Replace `AppStore` JSON domain persistence with EF Core/PostgreSQL domain DbContext.
2. Replace `AppStore` JSON domain persistence with EF Core/PostgreSQL domain DbContext.
3. Add jobs-list page bulk/actions UX for cancel/retry and permissions polish.
4. Tighten web auth (use actual cookie auth forwarding instead of `X-User-*` fallback for cross-process dev).

## Known design tradeoffs

- `RunnerAgentEntity.SecretValue` is stored in plaintext in JSON store (MVP simplification). `SecretHash` also stored and validated.
- SignalR subscriptions are client-side; some pages still do manual refresh for initial load and fallback.
- Domain and auth persistence are split (JSON + PostgreSQL) temporarily.
- Test integration uses SQLite only for Identity/auth DB; domain store remains JSON even in tests.
