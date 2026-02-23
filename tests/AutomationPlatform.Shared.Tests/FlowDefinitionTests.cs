using AutomationPlatform.Shared;

namespace AutomationPlatform.Shared.Tests;

public sealed class FlowDefinitionTests
{
    [Fact]
    public void CreateDefault_ReturnsRunProcessNode()
    {
        var definition = FlowDefinition.CreateDefault();

        var node = Assert.Single(definition.Nodes);
        Assert.Equal("n1", node.NodeId);
        Assert.Equal("RunProcess", node.BlockType);
        Assert.Equal(120, node.X);
        Assert.Equal(80, node.Y);
        Assert.Empty(definition.Edges);
        Assert.Equal("dotnet", JsonHelpers.GetString(node.Config, "path"));
        Assert.Equal("--info", JsonHelpers.GetString(node.Config, "args"));
        Assert.Equal(30, JsonHelpers.GetInt(node.Config, "timeoutSec"));
    }

    [Fact]
    public void NodeConfig_IsCaseInsensitive()
    {
        var node = new NodeDto();
        node.Config["Path"] = "dotnet";

        Assert.Equal("dotnet", JsonHelpers.GetString(node.Config, "path"));
    }
}
