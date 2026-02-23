namespace AutomationPlatform.Shared;

public enum JobStatus
{
    Queued = 0,
    Assigned = 1,
    Running = 2,
    Succeeded = 3,
    Failed = 4,
    Canceled = 5
}

public enum StepStatus
{
    Pending = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3,
    Skipped = 4,
    Canceled = 5
}

public enum RunnerStatus
{
    Offline = 0,
    Online = 1,
    Busy = 2,
    Disabled = 3
}

public enum LogLevelKind
{
    Trace = 0,
    Information = 1,
    Warning = 2,
    Error = 3
}
