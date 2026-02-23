namespace AutomationPlatform.Shared;

public sealed class JobDto
{
    public Guid Id { get; set; }
    public Guid FlowId { get; set; }
    public Guid FlowVersionId { get; set; }
    public string? FlowName { get; set; }
    public string RequestedByUserId { get; set; } = string.Empty;
    public Guid? AgentId { get; set; }
    public string? AgentName { get; set; }
    public JobStatus Status { get; set; }
    public DateTimeOffset QueuedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public long? DurationMs { get; set; }
    public string? ResultSummary { get; set; }
    public bool CancelRequested { get; set; }
}

public sealed class JobStepDto
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

public sealed class JobLogDto
{
    public long Id { get; set; }
    public Guid JobId { get; set; }
    public Guid? StepId { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public LogLevelKind Level { get; set; }
    public string Message { get; set; } = string.Empty;
}

public sealed class JobDetailsDto
{
    public JobDto Job { get; set; } = new();
    public List<JobStepDto> Steps { get; set; } = [];
    public List<JobLogDto> Logs { get; set; } = [];
}

public sealed class JobEventDto
{
    public string EventType { get; set; } = "Log";
    public Guid? StepId { get; set; }
    public string? NodeId { get; set; }
    public string? BlockType { get; set; }
    public StepStatus? StepStatus { get; set; }
    public int? ExitCode { get; set; }
    public string? OutputJson { get; set; }
    public LogLevelKind Level { get; set; } = LogLevelKind.Information;
    public string Message { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class JobEventsRequest
{
    public List<JobEventDto> Events { get; set; } = [];
}

public sealed class JobCompleteRequest
{
    public string? ResultSummary { get; set; }
}

public sealed class JobFailRequest
{
    public string Error { get; set; } = string.Empty;
}

public sealed class JobExecutionPayloadDto
{
    public Guid JobId { get; set; }
    public Guid FlowVersionId { get; set; }
    public string FlowName { get; set; } = string.Empty;
    public FlowDefinition Definition { get; set; } = new();
}
