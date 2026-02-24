using System.Text.Json.Serialization;

namespace tidus4400.Karakuri.Shared;

public sealed class FlowDefinition
{
    [JsonPropertyName("nodes")]
    public List<NodeDto> Nodes { get; set; } = [];

    [JsonPropertyName("edges")]
    public List<EdgeDto> Edges { get; set; } = [];

    public static FlowDefinition CreateDefault()
    {
        return new FlowDefinition
        {
            Nodes =
            [
                new NodeDto
                {
                    NodeId = "n1",
                    BlockType = "RunProcess",
                    DisplayName = "Launch",
                    X = 120,
                    Y = 80,
                    Config = new Dictionary<string, object?>
                    {
                        ["path"] = "dotnet",
                        ["args"] = "--info",
                        ["workingDir"] = string.Empty,
                        ["timeoutSec"] = 30
                    }
                }
            ]
        };
    }
}

public sealed class NodeDto
{
    public string NodeId { get; set; } = string.Empty;
    public string BlockType { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public double X { get; set; }
    public double Y { get; set; }
    public Dictionary<string, object?> Config { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class EdgeDto
{
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
}
