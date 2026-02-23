namespace AutomationPlatform.Shared;

public class FlowSummaryDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsEnabled { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int LatestVersionNumber { get; set; }
}

public sealed class FlowDto : FlowSummaryDto
{
    public string OwnerUserId { get; set; } = string.Empty;
}

public sealed class CreateFlowRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public sealed class UpdateFlowRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsEnabled { get; set; } = true;
}

public sealed class SaveFlowVersionRequest
{
    public FlowDefinition Definition { get; set; } = new();
}

public sealed class FlowVersionDto
{
    public Guid Id { get; set; }
    public Guid FlowId { get; set; }
    public int VersionNumber { get; set; }
    public FlowDefinition Definition { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class RunFlowRequest
{
    public Guid? PreferredRunnerId { get; set; }
}
