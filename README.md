# AutomationPlatform (Blazor + Orchestrator + Runner)

AutomationPlatform is a local MVP automation platform inspired by simplified PowerApps/Alteryx-style flow orchestration.

It includes:
- `AutomationPlatform.Orchestrator` (Minimal API + SignalR hub + auth + runner control)
- `AutomationPlatform.Web` (Blazor Server UI with custom CSS Grid UI, no external UI libs)
- `AutomationPlatform.Runner` (runner agent worker/console with local status endpoints and HMAC-signed pull protocol)
- `AutomationPlatform.Shared` (DTOs / flow definition / enums / HMAC helper)

## Important MVP notes (environment-driven deviations)

This repository was built in an offline package environment originally; package restore is now enabled again and the solution uses the proper Identity EF + SignalR client libraries. One major fallback remains to keep the MVP stable:

1. Domain persistence fallback:
- The orchestrator currently uses a file-backed JSON store (`/src/AutomationPlatform.Orchestrator/data/store.json`) instead of EF Core + SQL provider runtime.
- Entity shapes and API contracts are still aligned to the requested model.
- `docker/docker-compose.yml` (PostgreSQL) is included for the intended database deployment target.
- Authentication now uses ASP.NET Core Identity + EF stores (PostgreSQL via `Npgsql`).
- The web app now subscribes to the orchestrator SignalR hub for live updates.
- Identity auth migrations are scaffolded in `/src/AutomationPlatform.Orchestrator/Migrations/Auth`.

## Repository layout

```text
AutomationPlatform.sln
README.md
/docker
  docker-compose.yml
/src
  AutomationPlatform.Shared
  AutomationPlatform.Orchestrator
  AutomationPlatform.Web
  AutomationPlatform.Runner
```

## Seeded users

On first run the orchestrator seeds:
- Admin: `admin@local` / `Admin123!`
- User: `user@local` / `User123!`

## Docker quick start (full stack)

From the repo root, bring up PostgreSQL + Orchestrator + Web + Runner containers:

```bash
cd /Users/tidus4400/Projects/Karakuri
docker-compose up --build
```

Also supported (same stack definition under `/docker`):

```bash
cd /Users/tidus4400/Projects/Karakuri
docker compose -f /Users/tidus4400/Projects/Karakuri/docker/docker-compose.yml up --build
```

URLs:
- Web UI: `http://localhost:5020`
- Orchestrator health: `http://localhost:5010/api/health`
- PostgreSQL: `localhost:5432`

Notes:
- The orchestrator uses PostgreSQL for Identity/auth and a mounted JSON file volume for flow/job/runner domain data (`/data/store.json`).
- The runner container implements registration, HMAC heartbeat/polling, and `RunProcess` execution, but `Runner__RegistrationToken` is blank by default in compose.
- After creating a token in the UI, either:
  - restart the `runner` service with `Runner__RegistrationToken` set, or
  - run a one-off registration/execution container: `docker compose run --rm -e Runner__RegistrationToken=YOUR_TOKEN runner`
- If no token is configured, the runner starts and waits/retries until a token is provided.

## 1. Start DB (PostgreSQL target)

A PostgreSQL compose file is included (target architecture for later DB-backed persistence):

```bash
cd /Users/tidus4400/Projects/Karakuri/docker
docker compose up -d
```

Note:
- PostgreSQL is now required for Identity/auth storage on startup.
- Flow/job/runner domain data is still stored in the JSON file store in this iteration.

## 2. Run Orchestrator and Web

Open two terminals.

Terminal A (Orchestrator):

```bash
cd /Users/tidus4400/Projects/Karakuri
dotnet run --project /Users/tidus4400/Projects/Karakuri/src/AutomationPlatform.Orchestrator --urls http://localhost:5010
```

Terminal B (Web UI):

```bash
cd /Users/tidus4400/Projects/Karakuri
dotnet run --project /Users/tidus4400/Projects/Karakuri/src/AutomationPlatform.Web --urls http://localhost:5020
```

Open:
- Web UI: `http://localhost:5020`
- Orchestrator API health: `http://localhost:5010/api/health`

## 3. Login as admin

1. Open `http://localhost:5020/login`
2. Sign in with:
- Email: `admin@local`
- Password: `Admin123!`

## 4. Create a registration token (admin)

1. Go to `/tokens`
2. Click `Create one-time token`
3. Copy the token shown in the modal (it is shown once)

## 5. Configure and run Runner

Update runner config:

File: `/Users/tidus4400/Projects/Karakuri/src/AutomationPlatform.Runner/appsettings.json`

Set:
- `Runner:ServerUrl` to `http://localhost:5010`
- `Runner:RegistrationToken` to the token you created
- optionally `Runner:Name`

Then run:

```bash
cd /Users/tidus4400/Projects/Karakuri
dotnet run --project /Users/tidus4400/Projects/Karakuri/src/AutomationPlatform.Runner
```

What happens:
- Runner registers via `POST /api/agents/register`
- Receives `AgentId` + `AgentSecret`
- Stores them in `runner.credentials.json`
- Starts heartbeat + long-poll loop
- Executes `RunProcess` steps sequentially and posts logs/events/completion back to orchestrator
- Supports user-triggered cancel requests (polls cancel status and kills running process when cancellation is requested)

Runner local endpoints (Kestrel):
- `GET http://127.0.0.1:5180/health`
- `GET http://127.0.0.1:5180/version`
- `GET http://127.0.0.1:5180/status`

## 6. Create a flow with `RunProcess` node (`dotnet --info`)

1. In the Web UI, go to `/flows`
2. Create a new flow
3. In the builder (`/flows/{id}/builder`):
- Use the palette `RunProcess` node (drag or click Add)
- Select the node
- Set config:
  - `path`: `dotnet`
  - `args`: `--info`
  - `workingDir`: leave blank
  - `timeoutSec`: `30`
4. Click `Save Version`

## 7. Run flow and watch logs live

1. In the builder, click `Run Flow`
2. You’ll be redirected to `/jobs/{id}`
3. Watch:
- Job status
- Step status
- Live log panel (SignalR-driven updates in this build)

You can also view:
- `/dashboard` for KPIs and recent jobs
- `/jobs` for job history
- `/runners` (admin) for runner status cards

## HMAC runner auth (implemented)

All runner requests after registration include:
- `X-Agent-Id`
- `X-Timestamp`
- `X-Signature`

Signature uses:
- `HMACSHA256(secret, canonicalString)`

Canonical format:

```text
{METHOD}\n{PATH}\n{TIMESTAMP}\n{SHA256_HEX_OF_BODY}
```

The orchestrator validates:
- signature
- timestamp window (±300s)
- agent enabled/existing

## Job cancel support (implemented)

- User/API can request cancellation: `POST /api/jobs/{id}/cancel`
- Runner polls cancel status (HMAC): `GET /api/jobs/{jobId}/cancel-status`
- Runner reports canceled completion (HMAC): `POST /api/jobs/{jobId}/canceled`
- Web UI job details page includes a `Cancel Job` button for cancellable jobs (`Queued`, `Assigned`, `Running`)

## Manual acceptance checks (MVP)

- Admin login works (`admin@local`)
- User login works (`user@local`)
- Admin-only pages (`/runners`, `/tokens`) redirect non-admins
- Runner token registration works and runner appears in `/runners`
- Flow builder node drag + position persistence works via Save Version
- Manual run creates job; runner pulls and executes
- Job details show logs and step records updating live via SignalR

## Windows service (optional, minimal)

You can run the runner as a Windows service after publishing.

Example (PowerShell, admin):

```powershell
sc.exe create AutomationPlatformRunner binPath= "C:\path\to\AutomationPlatform.Runner.exe"
sc.exe start AutomationPlatformRunner
```

Notes:
- Configure `appsettings.json` (or environment vars) before installing
- Ensure the service account can execute the configured processes

## macOS launchd sample (optional)

Example plist (`~/Library/LaunchAgents/com.local.automationplatform.runner.plist`):

```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
  <dict>
    <key>Label</key>
    <string>com.local.automationplatform.runner</string>
    <key>ProgramArguments</key>
    <array>
      <string>/usr/local/share/dotnet/dotnet</string>
      <string>/Users/tidus4400/Projects/Karakuri/src/AutomationPlatform.Runner/bin/Debug/net10.0/AutomationPlatform.Runner.dll</string>
    </array>
    <key>WorkingDirectory</key>
    <string>/Users/tidus4400/Projects/Karakuri/src/AutomationPlatform.Runner</string>
    <key>RunAtLoad</key>
    <true />
    <key>KeepAlive</key>
    <true />
    <key>StandardOutPath</key>
    <string>/tmp/automationplatform-runner.out.log</string>
    <key>StandardErrorPath</key>
    <string>/tmp/automationplatform-runner.err.log</string>
  </dict>
</plist>
```

Load/unload:

```bash
launchctl load ~/Library/LaunchAgents/com.local.automationplatform.runner.plist
launchctl unload ~/Library/LaunchAgents/com.local.automationplatform.runner.plist
```

## Build

```bash
cd /Users/tidus4400/Projects/Karakuri
dotnet build /Users/tidus4400/Projects/Karakuri/AutomationPlatform.sln -p:NuGetAudit=false
```

## EF migration tooling (auth DB)

A local `dotnet-ef` tool manifest is included (`/Users/tidus4400/Projects/Karakuri/dotnet-tools.json`).

Examples:

```bash
cd /Users/tidus4400/Projects/Karakuri
dotnet tool restore
dotnet dotnet-ef migrations list --project src/AutomationPlatform.Orchestrator --startup-project src/AutomationPlatform.Orchestrator --context AuthIdentityDbContext
```
The orchestrator applies Identity auth migrations on startup (`Database.Migrate()`).
