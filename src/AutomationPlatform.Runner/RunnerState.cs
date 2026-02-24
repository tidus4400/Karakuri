namespace AutomationPlatform.Runner;

public sealed class RunnerState
{
    private readonly object _gate = new();

    public Guid? AgentId { get; private set; }
    public bool IsRegistered => AgentId.HasValue && !string.IsNullOrWhiteSpace(_agentSecret);
    private string _agentSecret = string.Empty;
    public string? CurrentJobName { get; private set; }
    public Guid? CurrentJobId { get; private set; }
    public int RunningJobs { get; private set; }
    public DateTimeOffset? LastHeartbeatAt { get; private set; }
    public DateTimeOffset? LastPollAt { get; private set; }
    public DateTimeOffset? LastSuccessfulJobAt { get; private set; }
    public DateTimeOffset? LastFailedJobAt { get; private set; }
    public string LastStatusMessage { get; private set; } = "Starting";
    public string? LastError { get; private set; }
    public DateTimeOffset? LastErrorAt { get; private set; }

    public void SetCredentials(RunnerCredentials credentials)
    {
        lock (_gate)
        {
            AgentId = credentials.AgentId;
            _agentSecret = credentials.AgentSecret;
            LastStatusMessage = "Registered";
            LastError = null;
            LastErrorAt = null;
        }
    }

    public void ClearCredentials()
    {
        lock (_gate)
        {
            AgentId = null;
            _agentSecret = string.Empty;
        }
    }

    public RunnerCredentials RequireCredentials()
    {
        lock (_gate)
        {
            if (!AgentId.HasValue || string.IsNullOrWhiteSpace(_agentSecret))
            {
                throw new InvalidOperationException("Runner is not registered.");
            }

            return new RunnerCredentials
            {
                AgentId = AgentId.Value,
                AgentSecret = _agentSecret
            };
        }
    }

    public void MarkHeartbeat(DateTimeOffset now, string? message = null)
    {
        lock (_gate)
        {
            LastHeartbeatAt = now;
            if (!string.IsNullOrWhiteSpace(message))
            {
                LastStatusMessage = message;
            }
        }
    }

    public void MarkPoll(DateTimeOffset now)
    {
        lock (_gate)
        {
            LastPollAt = now;
        }
    }

    public void MarkJobStarted(Guid jobId, string? flowName)
    {
        lock (_gate)
        {
            CurrentJobId = jobId;
            CurrentJobName = flowName;
            RunningJobs = 1;
            LastStatusMessage = $"Running job {jobId}";
        }
    }

    public void MarkJobSucceeded(Guid jobId)
    {
        lock (_gate)
        {
            if (CurrentJobId == jobId)
            {
                CurrentJobId = null;
                CurrentJobName = null;
            }
            RunningJobs = 0;
            LastSuccessfulJobAt = DateTimeOffset.UtcNow;
            LastStatusMessage = "Idle";
        }
    }

    public void MarkJobFailed(Guid jobId, string error)
    {
        lock (_gate)
        {
            if (CurrentJobId == jobId)
            {
                CurrentJobId = null;
                CurrentJobName = null;
            }
            RunningJobs = 0;
            LastFailedJobAt = DateTimeOffset.UtcNow;
            LastError = error;
            LastErrorAt = DateTimeOffset.UtcNow;
            LastStatusMessage = "Idle";
        }
    }

    public void SetStatus(string message)
    {
        lock (_gate)
        {
            LastStatusMessage = message;
        }
    }

    public void RecordError(Exception ex, string? context = null)
    {
        lock (_gate)
        {
            LastError = string.IsNullOrWhiteSpace(context) ? ex.Message : $"{context}: {ex.Message}";
            LastErrorAt = DateTimeOffset.UtcNow;
        }
    }

    public RunnerStatusSnapshot CreateSnapshot(RunnerOptions options)
    {
        lock (_gate)
        {
            return new RunnerStatusSnapshot
            {
                AgentId = AgentId,
                IsRegistered = IsRegistered,
                ServerUrl = options.ServerUrl,
                Name = string.IsNullOrWhiteSpace(options.Name) ? null : options.Name,
                Tags = options.Tags,
                RunningJobs = RunningJobs,
                CurrentJobId = CurrentJobId,
                CurrentJobName = CurrentJobName,
                LastHeartbeatAt = LastHeartbeatAt,
                LastPollAt = LastPollAt,
                LastSuccessfulJobAt = LastSuccessfulJobAt,
                LastFailedJobAt = LastFailedJobAt,
                LastStatusMessage = LastStatusMessage,
                LastError = LastError,
                LastErrorAt = LastErrorAt
            };
        }
    }
}

public sealed class RunnerStatusSnapshot
{
    public Guid? AgentId { get; set; }
    public bool IsRegistered { get; set; }
    public string ServerUrl { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? Tags { get; set; }
    public int RunningJobs { get; set; }
    public Guid? CurrentJobId { get; set; }
    public string? CurrentJobName { get; set; }
    public DateTimeOffset? LastHeartbeatAt { get; set; }
    public DateTimeOffset? LastPollAt { get; set; }
    public DateTimeOffset? LastSuccessfulJobAt { get; set; }
    public DateTimeOffset? LastFailedJobAt { get; set; }
    public string LastStatusMessage { get; set; } = string.Empty;
    public string? LastError { get; set; }
    public DateTimeOffset? LastErrorAt { get; set; }
}
