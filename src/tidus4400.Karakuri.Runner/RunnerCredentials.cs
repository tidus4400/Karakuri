namespace tidus4400.Karakuri.Runner;

public sealed class RunnerCredentials
{
    public Guid AgentId { get; set; }
    public string AgentSecret { get; set; } = string.Empty;
    public DateTimeOffset RegisteredAt { get; set; } = DateTimeOffset.UtcNow;
    public string? Name { get; set; }
}
