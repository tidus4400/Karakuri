using System.Security.Claims;
using System.Text.Json;
using tidus4400.Karakuri.Shared;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace tidus4400.Karakuri.Orchestrator;

public sealed class AppStore(IServiceScopeFactory scopeFactory)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private PlatformStateData _state = new();
    private bool _loaded;

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (!_loaded)
            {
                await LoadUnsafeAsync(ct);
                _loaded = true;
            }

            var dirty = false;
            if (!_state.Blocks.Any(b => b.OwnerUserId == "system" && b.Name == "RunProcess"))
            {
                _state.Blocks.Add(new BlockEntity
                {
                    Id = Guid.NewGuid(),
                    OwnerUserId = "system",
                    Name = "RunProcess",
                    Description = "Run a local process",
                    SchemaJson = "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\"},\"args\":{\"type\":\"string\"},\"workingDir\":{\"type\":\"string\"},\"timeoutSec\":{\"type\":\"integer\"}}}",
                    CreatedAt = DateTimeOffset.UtcNow
                });
                dirty = true;
            }

            if (dirty)
            {
                await SaveUnsafeAsync(ct);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<T> ReadAsync<T>(Func<PlatformStateData, T> func, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await EnsureLoadedUnsafeAsync(ct);
            return func(_state);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<T> WriteAsync<T>(Func<PlatformStateData, T> func, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await EnsureLoadedUnsafeAsync(ct);
            var result = func(_state);
            await SaveUnsafeAsync(ct);
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task WriteAsync(Action<PlatformStateData> action, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await EnsureLoadedUnsafeAsync(ct);
            action(_state);
            await SaveUnsafeAsync(ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureLoadedUnsafeAsync(CancellationToken ct)
    {
        if (_loaded) return;
        await LoadUnsafeAsync(ct);
        _loaded = true;
    }

    private async Task SaveUnsafeAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        db.JobLogs.RemoveRange(db.JobLogs);
        db.JobSteps.RemoveRange(db.JobSteps);
        db.Jobs.RemoveRange(db.Jobs);
        db.RegistrationTokens.RemoveRange(db.RegistrationTokens);
        db.RunnerAgents.RemoveRange(db.RunnerAgents);
        db.FlowVersions.RemoveRange(db.FlowVersions);
        db.Flows.RemoveRange(db.Flows);
        db.Blocks.RemoveRange(db.Blocks);
        await db.SaveChangesAsync(ct);

        await db.Blocks.AddRangeAsync(_state.Blocks, ct);
        await db.Flows.AddRangeAsync(_state.Flows, ct);
        await db.FlowVersions.AddRangeAsync(_state.FlowVersions, ct);
        await db.RunnerAgents.AddRangeAsync(_state.RunnerAgents, ct);
        await db.RegistrationTokens.AddRangeAsync(_state.RegistrationTokens, ct);
        await db.Jobs.AddRangeAsync(_state.Jobs, ct);
        await db.JobSteps.AddRangeAsync(_state.JobSteps, ct);
        await db.JobLogs.AddRangeAsync(_state.JobLogs, ct);
        await db.SaveChangesAsync(ct);

        await tx.CommitAsync(ct);
    }

    private async Task LoadUnsafeAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();

        _state = new PlatformStateData
        {
            Blocks = await db.Blocks.AsNoTracking().ToListAsync(ct),
            Flows = await db.Flows.AsNoTracking().ToListAsync(ct),
            FlowVersions = await db.FlowVersions.AsNoTracking().ToListAsync(ct),
            RunnerAgents = await db.RunnerAgents.AsNoTracking().ToListAsync(ct),
            RegistrationTokens = await db.RegistrationTokens.AsNoTracking().ToListAsync(ct),
            Jobs = await db.Jobs.AsNoTracking().ToListAsync(ct),
            JobSteps = await db.JobSteps.AsNoTracking().ToListAsync(ct),
            JobLogs = await db.JobLogs.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct)
        };
        _state.NextJobLogId = _state.JobLogs.Count == 0 ? 1 : _state.JobLogs.Max(x => x.Id) + 1;
    }
}

public sealed class PlatformStateData
{
    public List<AppUserEntity> Users { get; set; } = [];
    public List<BlockEntity> Blocks { get; set; } = [];
    public List<FlowEntity> Flows { get; set; } = [];
    public List<FlowVersionEntity> FlowVersions { get; set; } = [];
    public List<RunnerAgentEntity> RunnerAgents { get; set; } = [];
    public List<RegistrationTokenEntity> RegistrationTokens { get; set; } = [];
    public List<JobEntity> Jobs { get; set; } = [];
    public List<JobStepEntity> JobSteps { get; set; } = [];
    public List<JobLogEntity> JobLogs { get; set; } = [];
    public long NextJobLogId { get; set; } = 1;
}

public sealed class AppUserEntity
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Role { get; set; } = "User";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class BlockEntity
{
    public Guid Id { get; set; }
    public string OwnerUserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string SchemaJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }

    public BlockDto ToDto() => new()
    {
        Id = Id,
        Name = Name,
        Description = Description,
        SchemaJson = SchemaJson,
        CreatedAt = CreatedAt
    };
}

public sealed class FlowEntity
{
    public Guid Id { get; set; }
    public string OwnerUserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsEnabled { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public FlowDto ToDto(int latestVersion) => new()
    {
        Id = Id,
        OwnerUserId = OwnerUserId,
        Name = Name,
        Description = Description,
        IsEnabled = IsEnabled,
        CreatedAt = CreatedAt,
        UpdatedAt = UpdatedAt,
        LatestVersionNumber = latestVersion
    };
}

public sealed class FlowVersionEntity
{
    public Guid Id { get; set; }
    public Guid FlowId { get; set; }
    public int VersionNumber { get; set; }
    public string DefinitionJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }

    public FlowDefinition ParseDefinition()
    {
        try
        {
            return JsonSerializer.Deserialize<FlowDefinition>(DefinitionJson) ?? new FlowDefinition();
        }
        catch
        {
            return new FlowDefinition();
        }
    }

    public FlowVersionDto ToDto() => new()
    {
        Id = Id,
        FlowId = FlowId,
        VersionNumber = VersionNumber,
        Definition = ParseDefinition(),
        CreatedAt = CreatedAt
    };
}

public sealed class RunnerAgentEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Os { get; set; } = string.Empty;
    public string? Tags { get; set; }
    public RunnerStatus Status { get; set; }
    public DateTimeOffset? LastHeartbeatAt { get; set; }
    public string SecretHash { get; set; } = string.Empty;
    public string SecretValue { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public bool IsEnabled { get; set; } = true;

    public bool IsOnline(DateTimeOffset now) => IsEnabled && LastHeartbeatAt.HasValue && LastHeartbeatAt.Value >= now.AddSeconds(-60);

    public RunnerDto ToDto(DateTimeOffset now) => new()
    {
        Id = Id,
        Name = Name,
        Os = Os,
        Tags = Tags,
        Status = IsEnabled ? (IsOnline(now) ? Status : RunnerStatus.Offline) : RunnerStatus.Disabled,
        LastHeartbeatAt = LastHeartbeatAt,
        CreatedAt = CreatedAt,
        IsEnabled = IsEnabled
    };
}

public sealed class RegistrationTokenEntity
{
    public Guid Id { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public string CreatedByUserId { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
    public Guid? UsedByAgentId { get; set; }
}

public sealed class JobEntity
{
    public Guid Id { get; set; }
    public Guid FlowId { get; set; }
    public Guid FlowVersionId { get; set; }
    public string RequestedByUserId { get; set; } = string.Empty;
    public Guid? AgentId { get; set; }
    public JobStatus Status { get; set; }
    public DateTimeOffset QueuedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public long? DurationMs { get; set; }
    public string? ResultSummary { get; set; }
    public bool CancelRequested { get; set; }
}

public sealed class JobStepEntity
{
    public Guid Id { get; set; }
    public Guid JobId { get; set; }
    public string NodeId { get; set; } = string.Empty;
    public string BlockType { get; set; } = string.Empty;
    public StepStatus Status { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public int? ExitCode { get; set; }
    public string? OutputJson { get; set; }
}

public sealed class JobLogEntity
{
    public long Id { get; set; }
    public Guid JobId { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public LogLevelKind Level { get; set; }
    public string Message { get; set; } = string.Empty;
    public Guid? StepId { get; set; }
}

public static class StateMapping
{
    public static JobDto ToJobDto(this JobEntity job, PlatformStateData state)
    {
        var flow = state.Flows.FirstOrDefault(f => f.Id == job.FlowId);
        var runner = job.AgentId.HasValue ? state.RunnerAgents.FirstOrDefault(r => r.Id == job.AgentId.Value) : null;
        return new JobDto
        {
            Id = job.Id,
            FlowId = job.FlowId,
            FlowVersionId = job.FlowVersionId,
            FlowName = flow?.Name,
            RequestedByUserId = job.RequestedByUserId,
            AgentId = job.AgentId,
            AgentName = runner?.Name,
            Status = job.Status,
            QueuedAt = job.QueuedAt,
            StartedAt = job.StartedAt,
            FinishedAt = job.FinishedAt,
            DurationMs = job.DurationMs,
            ResultSummary = job.ResultSummary,
            CancelRequested = job.CancelRequested
        };
    }

    public static JobStepDto ToStepDto(this JobStepEntity step) => new()
    {
        Id = step.Id,
        JobId = step.JobId,
        NodeId = step.NodeId,
        BlockType = step.BlockType,
        Status = step.Status,
        StartedAt = step.StartedAt,
        FinishedAt = step.FinishedAt,
        ExitCode = step.ExitCode,
        OutputJson = step.OutputJson
    };

    public static JobLogDto ToLogDto(this JobLogEntity log) => new()
    {
        Id = log.Id,
        JobId = log.JobId,
        StepId = log.StepId,
        Timestamp = log.Timestamp,
        Level = log.Level,
        Message = log.Message
    };
}

public static class FlowOrdering
{
    public static List<NodeDto> Order(FlowDefinition definition)
    {
        if (definition.Edges.Count == 0)
        {
            return definition.Nodes.ToList();
        }

        var nodes = definition.Nodes.ToDictionary(n => n.NodeId, n => n);
        var indegree = definition.Nodes.ToDictionary(n => n.NodeId, _ => 0);
        var outgoing = definition.Nodes.ToDictionary(n => n.NodeId, _ => new List<string>());

        foreach (var edge in definition.Edges)
        {
            if (!nodes.ContainsKey(edge.From) || !nodes.ContainsKey(edge.To)) continue;
            outgoing[edge.From].Add(edge.To);
            indegree[edge.To]++;
        }

        var q = new Queue<string>(indegree.Where(kv => kv.Value == 0).Select(kv => kv.Key));
        var result = new List<NodeDto>();
        while (q.Count > 0)
        {
            var id = q.Dequeue();
            result.Add(nodes[id]);
            foreach (var next in outgoing[id])
            {
                indegree[next]--;
                if (indegree[next] == 0) q.Enqueue(next);
            }
        }

        return result.Count == definition.Nodes.Count ? result : definition.Nodes.ToList();
    }
}

public readonly record struct CurrentUser(string UserId, string Email, string Role)
{
    public bool IsAdmin => string.Equals(Role, "Admin", StringComparison.OrdinalIgnoreCase);
}

public static class HttpContextUserExtensions
{
    public static CurrentUser? GetCurrentUser(this HttpContext http)
    {
        if (http.User?.Identity?.IsAuthenticated == true)
        {
            var id = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            var email = http.User.FindFirstValue(ClaimTypes.Email) ?? http.User.Identity.Name;
            var role = http.User.FindFirstValue(ClaimTypes.Role) ?? "User";
            if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(email))
            {
                return new CurrentUser(id!, email!, role);
            }
        }

        if (http.Request.Headers.TryGetValue("X-User-Id", out var idHeader) &&
            http.Request.Headers.TryGetValue("X-User-Email", out var emailHeader))
        {
            var role = http.Request.Headers.TryGetValue("X-User-Role", out var roleHeader) ? roleHeader.ToString() : "User";
            return new CurrentUser(idHeader.ToString(), emailHeader.ToString(), role);
        }

        return null;
    }
}

public sealed class RoundRobinState
{
    private int _index = -1;
    private readonly object _gate = new();

    public Guid? Choose(IReadOnlyList<Guid> ids)
    {
        if (ids.Count == 0) return null;
        lock (_gate)
        {
            _index = (_index + 1) % ids.Count;
            return ids[_index];
        }
    }
}

public sealed class MonitoringHub : Hub
{
}

public static class HubPublishingExtensions
{
    public static Task PublishRunnerUpdatedAsync(this IHubContext<MonitoringHub> hub, RunnerDto dto)
        => hub.Clients.All.SendAsync("RunnerUpdated", dto);

    public static Task PublishJobUpdatedAsync(this IHubContext<MonitoringHub> hub, JobDto dto)
        => hub.Clients.All.SendAsync("JobUpdated", dto);

    public static Task PublishJobLogAppendedAsync(this IHubContext<MonitoringHub> hub, Guid jobId, JobLogDto log)
        => hub.Clients.All.SendAsync("JobLogAppended", jobId, log);
}

public sealed class RunnerOfflineMonitorService(AppStore store, IHubContext<MonitoringHub> hub, ILogger<RunnerOfflineMonitorService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var updated = await store.WriteAsync(state =>
                {
                    var now = DateTimeOffset.UtcNow;
                    var changes = new List<RunnerDto>();
                    foreach (var runner in state.RunnerAgents)
                    {
                        if (runner.IsEnabled && !runner.IsOnline(now) && runner.Status != RunnerStatus.Offline)
                        {
                            runner.Status = RunnerStatus.Offline;
                            changes.Add(runner.ToDto(now));
                        }
                    }
                    return changes;
                }, stoppingToken);

                foreach (var dto in updated)
                {
                    await hub.PublishRunnerUpdatedAsync(dto);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Runner offline monitor failed");
            }
        }
    }
}

public readonly record struct RunnerHmacAuth(bool IsValid, Guid AgentId, string SecretValue, IResult? Failure)
{
    public static RunnerHmacAuth Fail(IResult failure) => new(false, Guid.Empty, string.Empty, failure);
    public static RunnerHmacAuth Ok(Guid agentId, string secretValue) => new(true, agentId, secretValue, null);
}

public static class RunnerHmacValidator
{
    public static async Task<RunnerHmacAuth> ValidateAsync(HttpContext http, AppStore store, Guid? expectedAgentId = null, CancellationToken ct = default)
    {
        if (!http.Request.Headers.TryGetValue("X-Agent-Id", out var agentHeader) || !Guid.TryParse(agentHeader, out var agentId))
        {
            return RunnerHmacAuth.Fail(Results.Unauthorized());
        }

        if (expectedAgentId.HasValue && expectedAgentId.Value != agentId)
        {
            return RunnerHmacAuth.Fail(Results.Unauthorized());
        }

        if (!http.Request.Headers.TryGetValue("X-Timestamp", out var tsHeader) || !long.TryParse(tsHeader.ToString(), out var ts))
        {
            return RunnerHmacAuth.Fail(Results.Unauthorized());
        }

        if (!http.Request.Headers.TryGetValue("X-Signature", out var sigHeader))
        {
            return RunnerHmacAuth.Fail(Results.Unauthorized());
        }

        var nowSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (Math.Abs(nowSeconds - ts) > 300)
        {
            return RunnerHmacAuth.Fail(Results.Unauthorized());
        }

        var agent = await store.ReadAsync(state => state.RunnerAgents.FirstOrDefault(r => r.Id == agentId), ct);
        if (agent is null || !agent.IsEnabled)
        {
            return RunnerHmacAuth.Fail(Results.Unauthorized());
        }

        http.Request.EnableBuffering();
        if (http.Request.Body.CanSeek)
        {
            http.Request.Body.Position = 0;
        }
        await using var ms = new MemoryStream();
        await http.Request.Body.CopyToAsync(ms, ct);
        var body = ms.ToArray();
        if (http.Request.Body.CanSeek)
        {
            http.Request.Body.Position = 0;
        }

        var bodyHash = HmacSigning.ComputeBodySha256Hex(body);
        var canonical = HmacSigning.BuildCanonicalString(http.Request.Method, http.Request.Path.Value ?? string.Empty, ts.ToString(), bodyHash);
        var expectedSig = HmacSigning.ComputeSignatureBase64(agent.SecretValue, canonical);
        if (!HmacSigning.ConstantTimeEqualsBase64(expectedSig, sigHeader.ToString()))
        {
            return RunnerHmacAuth.Fail(Results.Unauthorized());
        }

        return RunnerHmacAuth.Ok(agentId, agent.SecretValue);
    }
}

public static class JobStepHelpers
{
    public static void EnsureSteps(PlatformStateData state, JobEntity job)
    {
        if (state.JobSteps.Any(s => s.JobId == job.Id))
        {
            return;
        }

        var version = state.FlowVersions.FirstOrDefault(v => v.Id == job.FlowVersionId);
        if (version is null) return;
        foreach (var node in FlowOrdering.Order(version.ParseDefinition()))
        {
            state.JobSteps.Add(new JobStepEntity
            {
                Id = Guid.NewGuid(),
                JobId = job.Id,
                NodeId = node.NodeId,
                BlockType = node.BlockType,
                Status = StepStatus.Pending
            });
        }
    }
}
