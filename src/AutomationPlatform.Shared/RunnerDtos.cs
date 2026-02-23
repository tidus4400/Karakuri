namespace AutomationPlatform.Shared;

public sealed class RunnerDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Os { get; set; } = string.Empty;
    public string? Tags { get; set; }
    public RunnerStatus Status { get; set; }
    public DateTimeOffset? LastHeartbeatAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public bool IsEnabled { get; set; } = true;
}

public sealed class RegistrationTokenDto
{
    public Guid Id { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
    public Guid? UsedByAgentId { get; set; }
    public bool IsExpired { get; set; }
}

public sealed class CreateRegistrationTokenResponse
{
    public Guid TokenId { get; set; }
    public string Token { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
}

public sealed class AgentRegisterRequest
{
    public string RegistrationToken { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Os { get; set; } = string.Empty;
    public string? Tags { get; set; }
}

public sealed class AgentRegisterResponse
{
    public Guid AgentId { get; set; }
    public string AgentSecret { get; set; } = string.Empty;
}

public sealed class HeartbeatRequest
{
    public string? StatusMessage { get; set; }
    public int RunningJobs { get; set; }
}
