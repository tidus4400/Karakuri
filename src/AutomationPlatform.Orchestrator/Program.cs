using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using AutomationPlatform.Orchestrator;
using AutomationPlatform.Shared;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});
var authConnectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Host=localhost;Port=5432;Database=automationplatform;Username=automation;Password=automation";
var authProvider = builder.Configuration["Auth:Provider"] ?? "Postgres";
builder.Services.AddDbContext<AuthIdentityDbContext>(options =>
{
    if (string.Equals(authProvider, "Sqlite", StringComparison.OrdinalIgnoreCase))
    {
        options.UseSqlite(authConnectionString);
    }
    else
    {
        options.UseNpgsql(authConnectionString);
    }
});
builder.Services.AddIdentity<IdentityAppUser, IdentityRole>(options =>
    {
        options.Password.RequireDigit = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireNonAlphanumeric = true;
        options.Password.RequiredLength = 8;
        options.User.RequireUniqueEmail = true;
    })
    .AddEntityFrameworkStores<AuthIdentityDbContext>()
    .AddDefaultTokenProviders();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "AutomationPlatform.Auth";
    options.LoginPath = "/login";
    options.Events.OnRedirectToLogin = ctx =>
    {
        if (ctx.Request.Path.StartsWithSegments("/api"))
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }
        ctx.Response.Redirect(ctx.RedirectUri);
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = ctx =>
    {
        if (ctx.Request.Path.StartsWithSegments("/api"))
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }
        ctx.Response.Redirect(ctx.RedirectUri);
        return Task.CompletedTask;
    };
});
builder.Services.AddAuthorization();
builder.Services.AddSignalR();
builder.Services.AddCors(options =>
{
    options.AddPolicy("local-dev", policy => policy
        .SetIsOriginAllowed(origin => origin.StartsWith("http://localhost:", StringComparison.OrdinalIgnoreCase))
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());
});
builder.Services.AddSingleton<AppStore>();
builder.Services.AddSingleton<RoundRobinState>();
builder.Services.AddHostedService<RunnerOfflineMonitorService>();

var app = builder.Build();

app.UseCors("local-dev");
app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.StartsWithSegments("/api/agents") || ctx.Request.Path.StartsWithSegments("/api/jobs"))
    {
        ctx.Request.EnableBuffering();
    }
    await next();
});
app.UseAuthentication();
app.UseAuthorization();

var store = app.Services.GetRequiredService<AppStore>();
await store.InitializeAsync();
await using (var scope = app.Services.CreateAsyncScope())
{
    var authDb = scope.ServiceProvider.GetRequiredService<AuthIdentityDbContext>();
    var authDbInitMode = builder.Configuration["Auth:DbInitMode"] ?? "Migrate";
    if (string.Equals(authDbInitMode, "EnsureCreated", StringComparison.OrdinalIgnoreCase))
    {
        await authDb.Database.EnsureCreatedAsync();
    }
    else
    {
        await authDb.Database.MigrateAsync();
    }
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityAppUser>>();
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    await SeedIdentityAsync(userManager, roleManager);
}

app.MapGet("/", () => Results.Redirect("/api/health"));
app.MapGet("/api/health", () => Results.Ok(new { status = "ok", utc = DateTimeOffset.UtcNow }));
app.MapHub<MonitoringHub>("/hubs/monitoring");

var api = app.MapGroup("/api");

api.MapPost("/auth/register", async (HttpContext http, UserManager<IdentityAppUser> userManager, RegisterUserRequest request) =>
{
    var current = http.GetCurrentUser();
    request.Email = request.Email.Trim();
    if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
    {
        return Results.BadRequest(new { error = "Email and password are required." });
    }

    var role = string.Equals(request.Role, "Admin", StringComparison.OrdinalIgnoreCase) && current?.IsAdmin == true ? "Admin" : "User";
    if (await userManager.FindByEmailAsync(request.Email) is not null)
    {
        return Results.Conflict(new { error = "Email already exists." });
    }

    var user = new IdentityAppUser
    {
        UserName = request.Email,
        Email = request.Email,
        EmailConfirmed = true,
        CreatedAt = DateTimeOffset.UtcNow
    };
    var createResult = await userManager.CreateAsync(user, request.Password);
    if (!createResult.Succeeded)
    {
        return Results.BadRequest(new { error = string.Join("; ", createResult.Errors.Select(e => e.Description)) });
    }
    var roleResult = await userManager.AddToRoleAsync(user, role);
    if (!roleResult.Succeeded)
    {
        return Results.BadRequest(new { error = string.Join("; ", roleResult.Errors.Select(e => e.Description)) });
    }

    return Results.Ok(new AuthUserDto { UserId = user.Id, Email = user.Email!, Role = role });
});

api.MapPost("/auth/login", async (HttpContext http, SignInManager<IdentityAppUser> signInManager, UserManager<IdentityAppUser> userManager, LoginRequest request) =>
{
    var user = await userManager.FindByEmailAsync(request.Email.Trim());
    if (user is null)
    {
        return Results.Unauthorized();
    }

    var signIn = await signInManager.PasswordSignInAsync(user, request.Password, isPersistent: false, lockoutOnFailure: false);
    if (!signIn.Succeeded)
    {
        return Results.Unauthorized();
    }
    var roles = await userManager.GetRolesAsync(user);
    var primaryRole = roles.FirstOrDefault() ?? "User";

    return Results.Ok(new LoginResponse
    {
        Success = true,
        User = new AuthUserDto { UserId = user.Id, Email = user.Email ?? string.Empty, Role = primaryRole }
    });
});

api.MapPost("/auth/logout", async (HttpContext http) =>
{
    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Ok();
});

api.MapGet("/auth/me", async (HttpContext http, UserManager<IdentityAppUser> userManager) =>
{
    var current = http.GetCurrentUser();
    if (current is null) return Results.Unauthorized();

    var user = await userManager.FindByIdAsync(current.Value.UserId);
    if (user is null) return Results.Unauthorized();
    var roles = await userManager.GetRolesAsync(user);
    return Results.Ok(new AuthUserDto { UserId = user.Id, Email = user.Email ?? string.Empty, Role = roles.FirstOrDefault() ?? current.Value.Role });
});

// Blocks
api.MapGet("/blocks", async (HttpContext http, AppStore appStore, CancellationToken ct) =>
{
    var current = http.GetCurrentUser();
    if (current is null) return Results.Unauthorized();

    var items = await appStore.ReadAsync(state =>
    {
        var query = state.Blocks.AsEnumerable();
        if (!current.Value.IsAdmin)
        {
            query = query.Where(b => b.OwnerUserId == current.Value.UserId || b.OwnerUserId == "system");
        }
        return query.OrderBy(b => b.Name).Select(b => b.ToDto()).ToList();
    }, ct);

    return Results.Ok(items);
});

api.MapPost("/blocks", async (HttpContext http, AppStore appStore, UpsertBlockRequest request, CancellationToken ct) =>
{
    var current = http.GetCurrentUser();
    if (current is null) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(request.Name)) return Results.BadRequest(new { error = "Name is required" });

    var dto = await appStore.WriteAsync(state =>
    {
        var entity = new BlockEntity
        {
            Id = Guid.NewGuid(),
            OwnerUserId = current.Value.UserId,
            Name = request.Name.Trim(),
            Description = request.Description,
            SchemaJson = string.IsNullOrWhiteSpace(request.SchemaJson) ? "{}" : request.SchemaJson,
            CreatedAt = DateTimeOffset.UtcNow
        };
        state.Blocks.Add(entity);
        return entity.ToDto();
    }, ct);

    return Results.Ok(dto);
});

api.MapGet("/blocks/{id:guid}", async (HttpContext http, AppStore appStore, Guid id, CancellationToken ct) =>
{
    var current = http.GetCurrentUser();
    if (current is null) return Results.Unauthorized();
    var result = await appStore.ReadAsync<IResult>(state =>
    {
        var entity = state.Blocks.FirstOrDefault(b => b.Id == id);
        if (entity is null) return Results.NotFound();
        if (!current.Value.IsAdmin && entity.OwnerUserId != current.Value.UserId && entity.OwnerUserId != "system") return Results.Forbid();
        return Results.Ok(entity.ToDto());
    }, ct);
    return result;
});

api.MapPut("/blocks/{id:guid}", async (HttpContext http, AppStore appStore, Guid id, UpsertBlockRequest request, CancellationToken ct) =>
{
    var current = http.GetCurrentUser();
    if (current is null) return Results.Unauthorized();
    var result = await appStore.WriteAsync<IResult>(state =>
    {
        var entity = state.Blocks.FirstOrDefault(b => b.Id == id);
        if (entity is null) return Results.NotFound();
        if (!current.Value.IsAdmin && entity.OwnerUserId != current.Value.UserId) return Results.Forbid();
        entity.Name = request.Name.Trim();
        entity.Description = request.Description;
        entity.SchemaJson = string.IsNullOrWhiteSpace(request.SchemaJson) ? "{}" : request.SchemaJson;
        return Results.Ok(entity.ToDto());
    }, ct);
    return result;
});

api.MapDelete("/blocks/{id:guid}", async (HttpContext http, AppStore appStore, Guid id, CancellationToken ct) =>
{
    var current = http.GetCurrentUser();
    if (current is null) return Results.Unauthorized();
    var result = await appStore.WriteAsync<IResult>(state =>
    {
        var entity = state.Blocks.FirstOrDefault(b => b.Id == id);
        if (entity is null) return Results.NotFound();
        if (!current.Value.IsAdmin && entity.OwnerUserId != current.Value.UserId) return Results.Forbid();
        state.Blocks.Remove(entity);
        return Results.Ok();
    }, ct);
    return result;
});

// Flows
api.MapGet("/flows", async (HttpContext http, AppStore appStore, CancellationToken ct) =>
{
    var current = http.GetCurrentUser();
    if (current is null) return Results.Unauthorized();

    var flows = await appStore.ReadAsync(state =>
    {
        var query = state.Flows.AsEnumerable();
        if (!current.Value.IsAdmin)
        {
            query = query.Where(f => f.OwnerUserId == current.Value.UserId);
        }

        return query.OrderByDescending(f => f.UpdatedAt)
            .Select(f => new FlowSummaryDto
            {
                Id = f.Id,
                Name = f.Name,
                Description = f.Description,
                IsEnabled = f.IsEnabled,
                CreatedAt = f.CreatedAt,
                UpdatedAt = f.UpdatedAt,
                LatestVersionNumber = state.FlowVersions.Where(v => v.FlowId == f.Id).Select(v => v.VersionNumber).DefaultIfEmpty(0).Max()
            })
            .ToList();
    }, ct);

    return Results.Ok(flows);
});

api.MapPost("/flows", async (HttpContext http, AppStore appStore, CreateFlowRequest request, CancellationToken ct) =>
{
    var current = http.GetCurrentUser();
    if (current is null) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(request.Name)) return Results.BadRequest(new { error = "Name is required" });

    var flow = await appStore.WriteAsync(state =>
    {
        var now = DateTimeOffset.UtcNow;
        var entity = new FlowEntity
        {
            Id = Guid.NewGuid(),
            OwnerUserId = current.Value.UserId,
            Name = request.Name.Trim(),
            Description = request.Description,
            IsEnabled = true,
            CreatedAt = now,
            UpdatedAt = now
        };
        state.Flows.Add(entity);
        state.FlowVersions.Add(new FlowVersionEntity
        {
            Id = Guid.NewGuid(),
            FlowId = entity.Id,
            VersionNumber = 1,
            DefinitionJson = JsonSerializer.Serialize(FlowDefinition.CreateDefault()),
            CreatedAt = now
        });
        return entity.ToDto(1);
    }, ct);

    return Results.Ok(flow);
});

api.MapGet("/flows/{id:guid}", async (HttpContext http, AppStore appStore, Guid id, CancellationToken ct) =>
{
    var current = http.GetCurrentUser();
    if (current is null) return Results.Unauthorized();
    var result = await appStore.ReadAsync<IResult>(state =>
    {
        var flow = state.Flows.FirstOrDefault(f => f.Id == id);
        if (flow is null) return Results.NotFound();
        if (!current.Value.IsAdmin && flow.OwnerUserId != current.Value.UserId) return Results.Forbid();
        var latest = state.FlowVersions.Where(v => v.FlowId == flow.Id).Select(v => v.VersionNumber).DefaultIfEmpty(0).Max();
        return Results.Ok(flow.ToDto(latest));
    }, ct);
    return result;
});

api.MapPut("/flows/{id:guid}", async (HttpContext http, AppStore appStore, Guid id, UpdateFlowRequest request, CancellationToken ct) =>
{
    var current = http.GetCurrentUser();
    if (current is null) return Results.Unauthorized();
    var result = await appStore.WriteAsync<IResult>(state =>
    {
        var flow = state.Flows.FirstOrDefault(f => f.Id == id);
        if (flow is null) return Results.NotFound();
        if (!current.Value.IsAdmin && flow.OwnerUserId != current.Value.UserId) return Results.Forbid();

        flow.Name = request.Name.Trim();
        flow.Description = request.Description;
        flow.IsEnabled = request.IsEnabled;
        flow.UpdatedAt = DateTimeOffset.UtcNow;
        var latest = state.FlowVersions.Where(v => v.FlowId == flow.Id).Select(v => v.VersionNumber).DefaultIfEmpty(0).Max();
        return Results.Ok(flow.ToDto(latest));
    }, ct);
    return result;
});

api.MapPost("/flows/{id:guid}/versions", async (HttpContext http, AppStore appStore, Guid id, SaveFlowVersionRequest request, CancellationToken ct) =>
{
    var current = http.GetCurrentUser();
    if (current is null) return Results.Unauthorized();
    var result = await appStore.WriteAsync<IResult>(state =>
    {
        var flow = state.Flows.FirstOrDefault(f => f.Id == id);
        if (flow is null) return Results.NotFound();
        if (!current.Value.IsAdmin && flow.OwnerUserId != current.Value.UserId) return Results.Forbid();

        var next = state.FlowVersions.Where(v => v.FlowId == id).Select(v => v.VersionNumber).DefaultIfEmpty(0).Max() + 1;
        var version = new FlowVersionEntity
        {
            Id = Guid.NewGuid(),
            FlowId = id,
            VersionNumber = next,
            DefinitionJson = JsonSerializer.Serialize(request.Definition ?? new FlowDefinition()),
            CreatedAt = DateTimeOffset.UtcNow
        };
        state.FlowVersions.Add(version);
        flow.UpdatedAt = DateTimeOffset.UtcNow;
        return Results.Ok(version.ToDto());
    }, ct);
    return result;
});

api.MapGet("/flows/{id:guid}/versions/latest", async (HttpContext http, AppStore appStore, Guid id, CancellationToken ct) =>
{
    var current = http.GetCurrentUser();
    if (current is null) return Results.Unauthorized();
    var result = await appStore.ReadAsync<IResult>(state =>
    {
        var flow = state.Flows.FirstOrDefault(f => f.Id == id);
        if (flow is null) return Results.NotFound();
        if (!current.Value.IsAdmin && flow.OwnerUserId != current.Value.UserId) return Results.Forbid();
        var version = state.FlowVersions.Where(v => v.FlowId == id).OrderByDescending(v => v.VersionNumber).FirstOrDefault();
        return version is null ? Results.NotFound() : Results.Ok(version.ToDto());
    }, ct);
    return result;
});

api.MapPost("/flows/{id:guid}/run", async (HttpContext http, AppStore appStore, IHubContext<MonitoringHub> hub, RoundRobinState rr, Guid id, RunFlowRequest? request, CancellationToken ct) =>
{
    var current = http.GetCurrentUser();
    if (current is null) return Results.Unauthorized();

    var (error, dto) = await appStore.WriteAsync(state =>
    {
        var flow = state.Flows.FirstOrDefault(f => f.Id == id);
        if (flow is null) return (Results.NotFound() as IResult, (JobDto?)null);
        if (!current.Value.IsAdmin && flow.OwnerUserId != current.Value.UserId) return (Results.Forbid() as IResult, (JobDto?)null);

        var version = state.FlowVersions.Where(v => v.FlowId == id).OrderByDescending(v => v.VersionNumber).FirstOrDefault();
        if (version is null) return (Results.BadRequest(new { error = "Flow has no versions." }) as IResult, (JobDto?)null);

        var now = DateTimeOffset.UtcNow;
        var online = state.RunnerAgents.Where(r => r.IsOnline(now) && r.IsEnabled).OrderBy(r => r.Name).ToList();
        Guid? agentId = request?.PreferredRunnerId;
        if (agentId is null && online.Count > 0)
        {
            agentId = rr.Choose(online.Select(r => r.Id).ToList());
        }

        var job = new JobEntity
        {
            Id = Guid.NewGuid(),
            FlowId = flow.Id,
            FlowVersionId = version.Id,
            RequestedByUserId = current.Value.UserId,
            AgentId = agentId,
            Status = agentId is null ? JobStatus.Queued : JobStatus.Assigned,
            QueuedAt = now
        };
        state.Jobs.Add(job);
        return ((IResult?)null, job.ToJobDto(state));
    }, ct);

    if (error is not null) return error;
    await hub.PublishJobUpdatedAsync(dto!);
    return Results.Ok(dto);
});

// Jobs
api.MapGet("/jobs", async (HttpContext http, AppStore appStore, CancellationToken ct) =>
{
    var current = http.GetCurrentUser();
    if (current is null) return Results.Unauthorized();
    var jobs = await appStore.ReadAsync(state =>
    {
        var query = state.Jobs.AsEnumerable();
        if (!current.Value.IsAdmin)
        {
            query = query.Where(j => j.RequestedByUserId == current.Value.UserId);
        }
        return query.OrderByDescending(j => j.QueuedAt).Take(200).Select(j => j.ToJobDto(state)).ToList();
    }, ct);
    return Results.Ok(jobs);
});

api.MapGet("/jobs/{id:guid}", async (HttpContext http, AppStore appStore, Guid id, CancellationToken ct) =>
{
    var current = http.GetCurrentUser();
    if (current is null) return Results.Unauthorized();

    var result = await appStore.ReadAsync<IResult>(state =>
    {
        var job = state.Jobs.FirstOrDefault(j => j.Id == id);
        if (job is null) return Results.NotFound();
        if (!current.Value.IsAdmin && job.RequestedByUserId != current.Value.UserId) return Results.Forbid();

        var details = new JobDetailsDto
        {
            Job = job.ToJobDto(state),
            Steps = state.JobSteps.Where(s => s.JobId == id).OrderBy(s => s.NodeId).Select(s => s.ToStepDto()).ToList(),
            Logs = state.JobLogs.Where(l => l.JobId == id).OrderBy(l => l.Id).Take(200).Select(l => l.ToLogDto()).ToList()
        };
        return Results.Ok(details);
    }, ct);

    return result;
});

api.MapGet("/jobs/{id:guid}/logs", async (HttpContext http, AppStore appStore, Guid id, int skip = 0, int take = 200, CancellationToken ct = default) =>
{
    var current = http.GetCurrentUser();
    if (current is null) return Results.Unauthorized();
    take = Math.Clamp(take, 1, 1000);
    skip = Math.Max(skip, 0);

    var result = await appStore.ReadAsync<IResult>(state =>
    {
        var job = state.Jobs.FirstOrDefault(j => j.Id == id);
        if (job is null) return Results.NotFound();
        if (!current.Value.IsAdmin && job.RequestedByUserId != current.Value.UserId) return Results.Forbid();

        var logs = state.JobLogs.Where(l => l.JobId == id).OrderBy(l => l.Id).Skip(skip).Take(take).Select(l => l.ToLogDto()).ToList();
        return Results.Ok(logs);
    }, ct);
    return result;
});

// Admin runners/tokens
api.MapGet("/runners", async (HttpContext http, AppStore appStore, CancellationToken ct) =>
{
    var current = http.GetCurrentUser();
    if (current is null) return Results.Unauthorized();
    if (!current.Value.IsAdmin) return Results.Forbid();

    var now = DateTimeOffset.UtcNow;
    var runners = await appStore.ReadAsync(state => state.RunnerAgents.OrderBy(r => r.Name).Select(r => r.ToDto(now)).ToList(), ct);
    return Results.Ok(runners);
});

api.MapPost("/tokens", async (HttpContext http, AppStore appStore, CancellationToken ct) =>
{
    var current = http.GetCurrentUser();
    if (current is null) return Results.Unauthorized();
    if (!current.Value.IsAdmin) return Results.Forbid();

    var plaintext = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
    var response = await appStore.WriteAsync(state =>
    {
        var entity = new RegistrationTokenEntity
        {
            Id = Guid.NewGuid(),
            TokenHash = HmacSigning.HashSecretForStorage(plaintext),
            CreatedByUserId = current.Value.UserId,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(2)
        };
        state.RegistrationTokens.Add(entity);
        return new CreateRegistrationTokenResponse
        {
            TokenId = entity.Id,
            Token = plaintext,
            ExpiresAt = entity.ExpiresAt
        };
    }, ct);

    return Results.Ok(response);
});

api.MapGet("/tokens", async (HttpContext http, AppStore appStore, CancellationToken ct) =>
{
    var current = http.GetCurrentUser();
    if (current is null) return Results.Unauthorized();
    if (!current.Value.IsAdmin) return Results.Forbid();

    var now = DateTimeOffset.UtcNow;
    var tokens = await appStore.ReadAsync(state => state.RegistrationTokens
        .OrderByDescending(t => t.ExpiresAt)
        .Select(t => new RegistrationTokenDto
        {
            Id = t.Id,
            ExpiresAt = t.ExpiresAt,
            UsedAt = t.UsedAt,
            UsedByAgentId = t.UsedByAgentId,
            IsExpired = t.ExpiresAt < now
        })
        .ToList(), ct);

    return Results.Ok(tokens);
});

// Runner registration
api.MapPost("/agents/register", async (AppStore appStore, IHubContext<MonitoringHub> hub, AgentRegisterRequest request, CancellationToken ct) =>
{
    var (error, response, runnerDto) = await appStore.WriteAsync(state =>
    {
        var tokenHash = HmacSigning.HashSecretForStorage(request.RegistrationToken.Trim());
        var token = state.RegistrationTokens.FirstOrDefault(t => t.TokenHash == tokenHash);
        if (token is null) return (Results.Unauthorized() as IResult, (AgentRegisterResponse?)null, (RunnerDto?)null);
        if (token.UsedAt is not null) return (Results.BadRequest(new { error = "Token already used." }) as IResult, (AgentRegisterResponse?)null, (RunnerDto?)null);
        if (token.ExpiresAt < DateTimeOffset.UtcNow) return (Results.BadRequest(new { error = "Token expired." }) as IResult, (AgentRegisterResponse?)null, (RunnerDto?)null);

        var now = DateTimeOffset.UtcNow;
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var runner = new RunnerAgentEntity
        {
            Id = Guid.NewGuid(),
            Name = string.IsNullOrWhiteSpace(request.Name) ? $"runner-{Guid.NewGuid():N}"[..12] : request.Name.Trim(),
            Os = string.IsNullOrWhiteSpace(request.Os) ? "unknown" : request.Os.Trim(),
            Tags = request.Tags,
            Status = RunnerStatus.Online,
            LastHeartbeatAt = now,
            SecretValue = secret,
            SecretHash = HmacSigning.HashSecretForStorage(secret),
            CreatedAt = now,
            IsEnabled = true
        };
        state.RunnerAgents.Add(runner);
        token.UsedAt = now;
        token.UsedByAgentId = runner.Id;

        return ((IResult?)null,
            new AgentRegisterResponse { AgentId = runner.Id, AgentSecret = secret },
            runner.ToDto(now));
    }, ct);

    if (error is not null) return error;
    await hub.PublishRunnerUpdatedAsync(runnerDto!);
    return Results.Ok(response);
});

api.MapPost("/agents/{agentId:guid}/heartbeat", async (HttpContext http, AppStore appStore, IHubContext<MonitoringHub> hub, Guid agentId, HeartbeatRequest request, CancellationToken ct) =>
{
    var auth = await RunnerHmacValidator.ValidateAsync(http, appStore, agentId, ct);
    if (!auth.IsValid) return auth.Failure!;

    var (result, runnerDto) = await appStore.WriteAsync(state =>
    {
        var runner = state.RunnerAgents.FirstOrDefault(r => r.Id == agentId);
        if (runner is null || !runner.IsEnabled) return (Results.Unauthorized() as IResult, (RunnerDto?)null);
        var now = DateTimeOffset.UtcNow;
        runner.LastHeartbeatAt = now;
        runner.Status = request.RunningJobs > 0 ? RunnerStatus.Busy : RunnerStatus.Online;
        var dto = runner.ToDto(now);
        return (Results.Ok(dto) as IResult, dto);
    }, ct);

    if (runnerDto is not null)
    {
        await hub.PublishRunnerUpdatedAsync(runnerDto);
    }
    return result;
});

api.MapGet("/agents/{agentId:guid}/jobs/next", async (HttpContext http, AppStore appStore, IHubContext<MonitoringHub> hub, Guid agentId, int waitSeconds = 45, CancellationToken ct = default) =>
{
    var auth = await RunnerHmacValidator.ValidateAsync(http, appStore, agentId, ct);
    if (!auth.IsValid) return auth.Failure!;

    waitSeconds = Math.Clamp(waitSeconds, 1, 45);
    var deadline = DateTimeOffset.UtcNow.AddSeconds(waitSeconds);

    while (DateTimeOffset.UtcNow < deadline && !ct.IsCancellationRequested)
    {
        var poll = await appStore.WriteAsync(state =>
        {
            var now = DateTimeOffset.UtcNow;
            var runner = state.RunnerAgents.FirstOrDefault(r => r.Id == agentId);
            if (runner is null || !runner.IsEnabled)
            {
                return LongPollOutcome.Invalid();
            }

            runner.LastHeartbeatAt = now;
            runner.Status = RunnerStatus.Busy;
            var runnerDto = runner.ToDto(now);

            var job = state.Jobs
                .Where(j => j.AgentId == agentId && (j.Status == JobStatus.Assigned || j.Status == JobStatus.Queued))
                .OrderBy(j => j.QueuedAt)
                .FirstOrDefault();

            var assignedChanged = false;
            if (job is null)
            {
                job = state.Jobs
                    .Where(j => j.AgentId is null && j.Status == JobStatus.Queued)
                    .OrderBy(j => j.QueuedAt)
                    .FirstOrDefault();
                if (job is not null)
                {
                    job.AgentId = agentId;
                    job.Status = JobStatus.Assigned;
                    assignedChanged = true;
                }
            }

            if (job is null)
            {
                return LongPollOutcome.Empty(runnerDto);
            }

            if (job.Status != JobStatus.Running)
            {
                job.Status = JobStatus.Running;
                job.StartedAt ??= now;
                JobStepHelpers.EnsureSteps(state, job);
            }

            var version = state.FlowVersions.FirstOrDefault(v => v.Id == job.FlowVersionId);
            if (version is null)
            {
                job.Status = JobStatus.Failed;
                job.FinishedAt = now;
                job.ResultSummary = "Flow version not found";
                return LongPollOutcome.WithJob(job.ToJobDto(state), runnerDto, null, assignedChanged);
            }

            var flow = state.Flows.FirstOrDefault(f => f.Id == job.FlowId);
            var payload = new JobExecutionPayloadDto
            {
                JobId = job.Id,
                FlowVersionId = job.FlowVersionId,
                FlowName = flow?.Name ?? "Flow",
                Definition = version.ParseDefinition()
            };
            return LongPollOutcome.WithJob(job.ToJobDto(state), runnerDto, payload, assignedChanged);
        }, ct);

        if (!poll.IsValid)
        {
            return Results.Unauthorized();
        }

        if (poll.RunnerUpdated is not null)
        {
            await hub.PublishRunnerUpdatedAsync(poll.RunnerUpdated);
        }
        if (poll.JobUpdated is not null)
        {
            await hub.PublishJobUpdatedAsync(poll.JobUpdated);
        }

        if (poll.Payload is not null)
        {
            return Results.Ok(poll.Payload);
        }

        await Task.Delay(TimeSpan.FromSeconds(1), ct);
    }

    var runnerAfter = await appStore.WriteAsync(state =>
    {
        var now = DateTimeOffset.UtcNow;
        var runner = state.RunnerAgents.FirstOrDefault(r => r.Id == agentId);
        if (runner is null) return (RunnerDto?)null;
        runner.LastHeartbeatAt = now;
        runner.Status = RunnerStatus.Online;
        return runner.ToDto(now);
    }, ct);
    if (runnerAfter is not null)
    {
        await hub.PublishRunnerUpdatedAsync(runnerAfter);
    }

    return Results.NoContent();
});

api.MapPost("/jobs/{jobId:guid}/events", async (HttpContext http, AppStore appStore, IHubContext<MonitoringHub> hub, Guid jobId, JobEventsRequest request, CancellationToken ct) =>
{
    var auth = await RunnerHmacValidator.ValidateAsync(http, appStore, null, ct);
    if (!auth.IsValid) return auth.Failure!;

    var update = await appStore.WriteAsync(state =>
    {
        var job = state.Jobs.FirstOrDefault(j => j.Id == jobId);
        if (job is null) return EventUpdateResult.NotFound();
        if (job.AgentId != auth.AgentId) return EventUpdateResult.Forbidden();

        var now = DateTimeOffset.UtcNow;
        var runner = state.RunnerAgents.FirstOrDefault(r => r.Id == auth.AgentId);
        if (runner is not null)
        {
            runner.LastHeartbeatAt = now;
            runner.Status = RunnerStatus.Busy;
        }

        var logs = new List<JobLogDto>();
        var jobChanged = false;
        foreach (var evt in request.Events)
        {
            JobStepEntity? step = null;
            if (!string.IsNullOrWhiteSpace(evt.NodeId))
            {
                step = state.JobSteps.FirstOrDefault(s => s.JobId == jobId && s.NodeId == evt.NodeId);
                if (step is not null && evt.StepStatus.HasValue)
                {
                    step.Status = evt.StepStatus.Value;
                    if (evt.StepStatus == StepStatus.Running)
                    {
                        step.StartedAt ??= evt.Timestamp == default ? now : evt.Timestamp;
                    }
                    if (evt.StepStatus is StepStatus.Succeeded or StepStatus.Failed or StepStatus.Canceled)
                    {
                        step.FinishedAt = evt.Timestamp == default ? now : evt.Timestamp;
                        step.ExitCode = evt.ExitCode;
                        step.OutputJson = evt.OutputJson;
                    }
                    jobChanged = true;
                }
            }

            var log = new JobLogEntity
            {
                Id = state.NextJobLogId++,
                JobId = jobId,
                StepId = step?.Id,
                Timestamp = evt.Timestamp == default ? now : evt.Timestamp,
                Level = evt.Level,
                Message = evt.Message
            };
            state.JobLogs.Add(log);
            logs.Add(log.ToLogDto());
        }

        var runnerDto = runner?.ToDto(now);
        var jobDto = jobChanged ? job.ToJobDto(state) : null;
        return EventUpdateResult.Ok(logs, jobDto, runnerDto);
    }, ct);

    return update.Kind switch
    {
        EventUpdateKind.NotFound => Results.NotFound(),
        EventUpdateKind.Forbidden => Results.Forbid(),
        _ => await PublishEventUpdateAsync(update, hub, jobId)
    };
});

api.MapPost("/jobs/{jobId:guid}/complete", async (HttpContext http, AppStore appStore, IHubContext<MonitoringHub> hub, Guid jobId, JobCompleteRequest request, CancellationToken ct) =>
{
    var auth = await RunnerHmacValidator.ValidateAsync(http, appStore, null, ct);
    if (!auth.IsValid) return auth.Failure!;

    var (error, jobDto, runnerDto) = await appStore.WriteAsync(state =>
    {
        var job = state.Jobs.FirstOrDefault(j => j.Id == jobId);
        if (job is null) return (Results.NotFound() as IResult, (JobDto?)null, (RunnerDto?)null);
        if (job.AgentId != auth.AgentId) return (Results.Forbid() as IResult, (JobDto?)null, (RunnerDto?)null);

        var now = DateTimeOffset.UtcNow;
        job.Status = JobStatus.Succeeded;
        job.FinishedAt = now;
        job.DurationMs = job.StartedAt.HasValue ? (long)(now - job.StartedAt.Value).TotalMilliseconds : null;
        job.ResultSummary = request.ResultSummary;

        var runner = state.RunnerAgents.FirstOrDefault(r => r.Id == auth.AgentId);
        if (runner is not null)
        {
            runner.LastHeartbeatAt = now;
            runner.Status = RunnerStatus.Online;
        }

        return ((IResult?)null, job.ToJobDto(state), runner?.ToDto(now));
    }, ct);

    if (error is not null) return error;
    await hub.PublishJobUpdatedAsync(jobDto!);
    if (runnerDto is not null) await hub.PublishRunnerUpdatedAsync(runnerDto);
    return Results.Ok(jobDto);
});

api.MapPost("/jobs/{jobId:guid}/fail", async (HttpContext http, AppStore appStore, IHubContext<MonitoringHub> hub, Guid jobId, JobFailRequest request, CancellationToken ct) =>
{
    var auth = await RunnerHmacValidator.ValidateAsync(http, appStore, null, ct);
    if (!auth.IsValid) return auth.Failure!;

    var (error, jobDto, runnerDto, logDto) = await appStore.WriteAsync(state =>
    {
        var job = state.Jobs.FirstOrDefault(j => j.Id == jobId);
        if (job is null) return (Results.NotFound() as IResult, (JobDto?)null, (RunnerDto?)null, (JobLogDto?)null);
        if (job.AgentId != auth.AgentId) return (Results.Forbid() as IResult, (JobDto?)null, (RunnerDto?)null, (JobLogDto?)null);

        var now = DateTimeOffset.UtcNow;
        job.Status = JobStatus.Failed;
        job.FinishedAt = now;
        job.DurationMs = job.StartedAt.HasValue ? (long)(now - job.StartedAt.Value).TotalMilliseconds : null;
        job.ResultSummary = request.Error;

        var log = new JobLogEntity
        {
            Id = state.NextJobLogId++,
            JobId = jobId,
            Timestamp = now,
            Level = LogLevelKind.Error,
            Message = request.Error
        };
        state.JobLogs.Add(log);

        var runner = state.RunnerAgents.FirstOrDefault(r => r.Id == auth.AgentId);
        if (runner is not null)
        {
            runner.LastHeartbeatAt = now;
            runner.Status = RunnerStatus.Online;
        }

        return ((IResult?)null, job.ToJobDto(state), runner?.ToDto(now), log.ToLogDto());
    }, ct);

    if (error is not null) return error;
    if (logDto is not null) await hub.PublishJobLogAppendedAsync(jobId, logDto);
    await hub.PublishJobUpdatedAsync(jobDto!);
    if (runnerDto is not null) await hub.PublishRunnerUpdatedAsync(runnerDto);
    return Results.Ok(jobDto);
});

app.Run();

static async Task SeedIdentityAsync(UserManager<IdentityAppUser> userManager, RoleManager<IdentityRole> roleManager)
{
    foreach (var role in new[] { "Admin", "User" })
    {
        if (!await roleManager.RoleExistsAsync(role))
        {
            await roleManager.CreateAsync(new IdentityRole(role));
        }
    }

    async Task EnsureUserAsync(string email, string password, string role)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            user = new IdentityAppUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                CreatedAt = DateTimeOffset.UtcNow
            };
            var created = await userManager.CreateAsync(user, password);
            if (!created.Succeeded)
            {
                throw new InvalidOperationException("Failed to seed user " + email + ": " + string.Join("; ", created.Errors.Select(e => e.Description)));
            }
        }

        if (!await userManager.IsInRoleAsync(user, role))
        {
            var added = await userManager.AddToRoleAsync(user, role);
            if (!added.Succeeded)
            {
                throw new InvalidOperationException("Failed to seed role for " + email + ": " + string.Join("; ", added.Errors.Select(e => e.Description)));
            }
        }
    }

    await EnsureUserAsync("admin@local", "Admin123!", "Admin");
    await EnsureUserAsync("user@local", "User123!", "User");
}

static async Task<IResult> PublishEventUpdateAsync(EventUpdateResult update, IHubContext<MonitoringHub> hub, Guid jobId)
{
    foreach (var log in update.Logs)
    {
        await hub.PublishJobLogAppendedAsync(jobId, log);
    }
    if (update.JobUpdated is not null)
    {
        await hub.PublishJobUpdatedAsync(update.JobUpdated);
    }
    if (update.RunnerUpdated is not null)
    {
        await hub.PublishRunnerUpdatedAsync(update.RunnerUpdated);
    }
    return Results.Ok();
}

internal sealed record LongPollOutcome(bool IsValid, JobDto? JobUpdated, RunnerDto? RunnerUpdated, JobExecutionPayloadDto? Payload)
{
    public static LongPollOutcome Invalid() => new(false, null, null, null);
    public static LongPollOutcome Empty(RunnerDto? runner) => new(true, null, runner, null);
    public static LongPollOutcome WithJob(JobDto? job, RunnerDto? runner, JobExecutionPayloadDto? payload, bool assignedChanged)
        => new(true, job, runner, payload);
}

internal enum EventUpdateKind
{
    Ok,
    NotFound,
    Forbidden
}

internal sealed record EventUpdateResult(EventUpdateKind Kind, List<JobLogDto> Logs, JobDto? JobUpdated, RunnerDto? RunnerUpdated)
{
    public static EventUpdateResult Ok(List<JobLogDto> logs, JobDto? job, RunnerDto? runner) => new(EventUpdateKind.Ok, logs, job, runner);
    public static EventUpdateResult NotFound() => new(EventUpdateKind.NotFound, [], null, null);
    public static EventUpdateResult Forbidden() => new(EventUpdateKind.Forbidden, [], null, null);
}

public partial class Program
{
}
