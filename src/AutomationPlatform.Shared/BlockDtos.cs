namespace AutomationPlatform.Shared;

public sealed class BlockDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string SchemaJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class UpsertBlockRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string SchemaJson { get; set; } = "{}";
}
